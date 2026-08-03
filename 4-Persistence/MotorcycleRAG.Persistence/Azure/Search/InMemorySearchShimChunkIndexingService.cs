using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.Persistence.Azure.Search;

/// <summary>
/// Sends production-shaped search chunk JSONL to a local in-memory Azure Search shim.
/// </summary>
public sealed class InMemorySearchShimChunkIndexingService : IChunkIndexingService
{
    private readonly HttpClient _httpClient;
    private readonly SearchOptions _options;
    private readonly ILogger<InMemorySearchShimChunkIndexingService> _logger;

    public InMemorySearchShimChunkIndexingService(
        HttpClient httpClient,
        IOptions<SearchOptions> options,
        ILogger<InMemorySearchShimChunkIndexingService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ChunkIndexingResult> IndexFromJsonlAsync(
        Stream jsonlStream,
        string uploadId,
        Guid indexedArtifactId,
        Guid ingestionJobId,
        string? sourceContentHash,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(jsonlStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

        if (!Uri.TryCreate(_options.InMemoryShimEndpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException(
                "Search:InMemoryShimEndpoint must be configured when using the in-memory search shim.");
        }
        if (!IsLoopbackHttpEndpoint(endpoint))
        {
            throw new InvalidOperationException(
                "Search:InMemoryShimEndpoint must be an http or https loopback URL when using the in-memory search shim.");
        }

        using var reader = new StreamReader(jsonlStream, Encoding.UTF8, leaveOpen: true);
        var jsonl = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        var parsed = ParseOutcomes(jsonl);

        // D3: Stamp every parsed record with the resolved anchor metadata before posting.
        var stampedJsonl = StampAnchorMetadata(jsonl, indexedArtifactId, ingestionJobId, sourceContentHash);

        var baseEndpoint = endpoint.AbsoluteUri.EndsWith('/')
            ? endpoint
            : new Uri(endpoint.AbsoluteUri + "/");
        var uriBuilder = new UriBuilder(new Uri(baseEndpoint, "index-jsonl"));
        uriBuilder.Query = $"uploadId={Uri.EscapeDataString(uploadId)}";

        using var content = new StringContent(stampedJsonl, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");

        using var response = await _httpClient.PostAsync(uriBuilder.Uri, content, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var reason = $"Search shim indexing failed: HTTP {(int)response.StatusCode} {detail[..Math.Min(detail.Length, 500)]}";
            _logger.LogError(
                "In-memory search shim rejected chunks for upload {UploadId}: {Reason}",
                LogSanitizer.Sanitize(uploadId),  // codeql[cs/log-forging]
                reason);

            return new ChunkIndexingResult(
                parsed.Count,
                parsed.Count > 0 ? 1 : 0,
                parsed.Select(outcome => outcome with { Succeeded = false, FailureReason = reason }).ToList());
        }

        _logger.LogInformation(
            "Indexed {ChunkCount} chunks into in-memory search shim for upload {UploadId}.",
            parsed.Count,
            LogSanitizer.Sanitize(uploadId));  // codeql[cs/log-forging]

        return new ChunkIndexingResult(parsed.Count, parsed.Count > 0 ? 1 : 0, parsed);
    }

    private static string StampAnchorMetadata(
        string jsonl,
        Guid indexedArtifactId,
        Guid ingestionJobId,
        string? sourceContentHash)
    {
        var stampedLines = new List<string>();
        foreach (var line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Deserialize the line into a mutable dictionary
            var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(line)
                       ?? new Dictionary<string, object?>();

            // Stamp the anchor fields using the same semantics as ChunkIndexingService:
            // Guid.Empty -> omit the key (not null), empty string -> omit the key (not null)
            // This prevents the legacy 3-parameter overload from wiping anchors on merge.
            if (indexedArtifactId != Guid.Empty)
            {
                dict["indexedArtifactId"] = indexedArtifactId.ToString();
            }
            else
            {
                // Remove the key entirely if unset, rather than setting it to null
                dict.Remove("indexedArtifactId");
            }

            if (ingestionJobId != Guid.Empty)
            {
                dict["ingestionJobId"] = ingestionJobId.ToString();
            }
            else
            {
                // Remove the key entirely if unset, rather than setting it to null
                dict.Remove("ingestionJobId");
            }

            if (!string.IsNullOrEmpty(sourceContentHash))
            {
                dict["sourceContentHash"] = sourceContentHash;
            }
            else
            {
                // Remove the key entirely if unset, rather than setting it to null
                dict.Remove("sourceContentHash");
            }

            // Re-serialize and add to the list
            var stampedLine = JsonSerializer.Serialize(dict);
            stampedLines.Add(stampedLine);
        }

        return string.Join("\n", stampedLines);
    }

    private static IReadOnlyList<ChunkIndexOutcome> ParseOutcomes(string jsonl)
    {
        var outcomes = new List<ChunkIndexOutcome>();
        foreach (var line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new JsonException("Search chunk record is missing required field 'id'.");
            }

            outcomes.Add(new ChunkIndexOutcome(
                id,
                true,
                null,
                root.TryGetProperty("pageNumber", out var pageElement) && pageElement.TryGetInt32(out var pageNumber) ? pageNumber : 0,
                root.TryGetProperty("chunkIndex", out var chunkElement) && chunkElement.TryGetInt32(out var chunkIndex) ? chunkIndex : 0,
                root.TryGetProperty("sourceFile", out var sourceElement) ? sourceElement.GetString() : null));
        }

        return outcomes;
    }

    private static bool IsLoopbackHttpEndpoint(Uri endpoint)
    {
        if (endpoint.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return System.Net.IPAddress.TryParse(endpoint.Host, out var address)
            && System.Net.IPAddress.IsLoopback(address);
    }
}
