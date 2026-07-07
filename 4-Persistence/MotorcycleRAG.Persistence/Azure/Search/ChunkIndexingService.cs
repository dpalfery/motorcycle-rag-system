using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Search.Documents;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.Persistence.Azure.Search;

/// <summary>
/// Indexes pre-embedded chunk records from a JSONL stream directly into Azure AI Search.
/// </summary>
public sealed class ChunkIndexingService : IChunkIndexingService
{
    private readonly SearchClient _searchClient;
    private readonly ILogger<ChunkIndexingService> _logger;

    /// <summary>
    /// Batch size for bulk indexing operations.
    /// </summary>
    private const int BatchSize = 100;

    public ChunkIndexingService(SearchClient searchClient, ILogger<ChunkIndexingService> logger)
    {
        _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reads a JSONL stream line-by-line and upserts each chunk into Azure AI Search.
    /// Returns detailed result with total parsed and per-chunk success/failure outcomes.
    /// </summary>
    public async Task<ChunkIndexingResult> IndexFromJsonlAsync(Stream jsonlStream, string uploadId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(jsonlStream);
        ArgumentNullException.ThrowIfNull(uploadId);

        int totalProcessed = 0;
        var outcomes = new List<ChunkIndexOutcome>();
        var batch = new List<ChunkIndexRecord>(BatchSize);

        using var reader = new StreamReader(jsonlStream);
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var chunk = JsonSerializer.Deserialize<ChunkIndexRecord>(line);
                if (chunk is not null)
                {
                    batch.Add(chunk);
                    totalProcessed++;

                    if (batch.Count >= BatchSize)
                    {
                        var batchOutcomes = await IndexBatchAsync(batch, ct).ConfigureAwait(false);
                        outcomes.AddRange(batchOutcomes);
                        batch.Clear();
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning(
                    jsonEx,
                    "Failed to parse chunk line for upload {UploadId}. Line will be skipped.",
                    LogSanitizer.Sanitize(uploadId));
            }
        }

        // Index remaining batch
        if (batch.Count > 0)
        {
            var batchOutcomes = await IndexBatchAsync(batch, ct).ConfigureAwait(false);
            outcomes.AddRange(batchOutcomes);
        }

        _logger.LogInformation(
            "Indexed {SuccessCount}/{TotalCount} chunks for upload {UploadId}.",
            outcomes.Count(o => o.Succeeded),
            totalProcessed,
            LogSanitizer.Sanitize(uploadId));

        return new ChunkIndexingResult(totalProcessed, outcomes);
    }

    /// <summary>
    /// Indexes a batch of chunk records into Azure AI Search, returning per-chunk outcomes.
    /// </summary>
    private async Task<IReadOnlyList<ChunkIndexOutcome>> IndexBatchAsync(List<ChunkIndexRecord> batch, CancellationToken ct)
    {
        var outcomes = new List<ChunkIndexOutcome>();

        try
        {
            var result = await _searchClient.MergeOrUploadDocumentsAsync(batch, cancellationToken: ct).ConfigureAwait(false);

            // Map result.Value.Results to capture per-chunk outcomes
            foreach (var indexResult in result.Value.Results)
            {
                var chunk = batch.FirstOrDefault(c => c.Id == indexResult.Key);
                if (chunk is not null)
                {
                    outcomes.Add(new ChunkIndexOutcome(
                        ChunkId: indexResult.Key,
                        Succeeded: indexResult.Succeeded,
                        FailureReason: indexResult.ErrorMessage,
                        PageNumber: chunk.PageNumber,
                        ChunkIndex: chunk.ChunkIndex,
                        SourceFile: chunk.SourceFile));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error indexing batch of {BatchSize} chunks into Azure AI Search.",
                batch.Count);

            // On exception, mark all chunks in batch as failed
            foreach (var chunk in batch)
            {
                outcomes.Add(new ChunkIndexOutcome(
                    ChunkId: chunk.Id,
                    Succeeded: false,
                    FailureReason: ex.Message,
                    PageNumber: chunk.PageNumber,
                    ChunkIndex: chunk.ChunkIndex,
                    SourceFile: chunk.SourceFile));
            }
        }

        return outcomes;
    }

    /// <summary>
    /// Represents a chunk record as stored in Azure AI Search.
    /// Field names via [JsonPropertyName] must match the Azure Search index schema.
    /// </summary>
    private sealed record ChunkIndexRecord
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; init; } = string.Empty;

        [JsonPropertyName("documentType")]
        public string DocumentType { get; init; } = string.Empty;

        [JsonPropertyName("make")]
        public string? Make { get; init; }

        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("year")]
        public int Year { get; init; }

        [JsonPropertyName("sourceFile")]
        public string? SourceFile { get; init; }

        [JsonPropertyName("section")]
        public string? Section { get; init; }

        [JsonPropertyName("pageNumber")]
        public int PageNumber { get; init; }

        [JsonPropertyName("pageRange")]
        public string? PageRange { get; init; }

        [JsonPropertyName("primarySection")]
        public string? PrimarySection { get; init; }

        [JsonPropertyName("sectionLevel")]
        public int SectionLevel { get; init; }

        [JsonPropertyName("sectionHeadings")]
        public List<string> SectionHeadings { get; init; } = [];

        [JsonPropertyName("tableCaption")]
        public string? TableCaption { get; init; }

        [JsonPropertyName("chunkIndex")]
        public int ChunkIndex { get; init; }

        [JsonPropertyName("tags")]
        public List<string> Tags { get; init; } = [];

        [JsonPropertyName("contentVector")]
        public float[] ContentVector { get; init; } = [];

        [JsonPropertyName("createdAt")]
        public DateTimeOffset CreatedAt { get; init; }

        [JsonPropertyName("updatedAt")]
        public DateTimeOffset UpdatedAt { get; init; }
    }
}
