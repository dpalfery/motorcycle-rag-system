using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.ExternalServices;

/// <summary>
/// Calls the Microsoft Fabric REST API to trigger and monitor ingestion pipeline runs.
/// Uses DefaultAzureCredential for authentication — no secrets in code.
/// Fabric base URL is read from the MCR_API_FABRIC_WORKSPACE_ENDPOINT environment variable.
/// Pipeline item IDs are read from MCR_API_FABRIC_PDF_PIPELINE_ID / MCR_API_FABRIC_CSV_PIPELINE_ID.
/// </summary>
#pragma warning disable S1133 // Intentionally kept as deprecated fallback
[Obsolete("Use ILocalPipelineService and LocalPipelineService. FabricPipelineService is kept as a fallback for ProcessingMode.Fabric.", false)]
#pragma warning restore S1133
public sealed class FabricPipelineService : IFabricPipelineService
{
    // Fabric REST API version and scope required for acquiring tokens.
    private const string FabricApiVersion = "v1";
    private const string FabricTokenScope = "https://api.fabric.microsoft.com/.default";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FabricIngestionOptions _config;
    private readonly TokenCredential _credential;
    private readonly ILogger<FabricPipelineService> _logger;

    public FabricPipelineService(
        IHttpClientFactory httpClientFactory,
        IOptions<FabricIngestionOptions> config,
        ILogger<FabricPipelineService> logger)
        : this(httpClientFactory, config, new DefaultAzureCredential(), logger)
    {
    }

    // Internal constructor to allow injecting a test credential in unit tests.
    internal FabricPipelineService(
        IHttpClientFactory httpClientFactory,
        IOptions<FabricIngestionOptions> config,
        TokenCredential credential,
        ILogger<FabricPipelineService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(config.Value.WorkspaceEndpoint))
            throw new InvalidOperationException(
                "FabricIngestion:WorkspaceEndpoint is required. " +
                "Set it via the MCR_API_FABRIC_WORKSPACE_ENDPOINT environment variable.");

        _httpClientFactory = httpClientFactory;
        _config = config.Value;
        _credential = credential;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> TriggerPipelineAsync(
        string uploadId,
        string documentType,
        string pipelineId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineId);

        _logger.LogInformation(
            "Triggering Fabric pipeline {PipelineId} for document type {DocumentType}",
            pipelineId, documentType);

        var body = JsonSerializer.Serialize(new
        {
            executionData = new
            {
                parameters = new
                {
                    uploadId,
                    documentType
                }
            }
        });

        var url = $"{_config.WorkspaceEndpoint.TrimEnd('/')}/pipelines/{pipelineId}/jobs/instances?api-version={FabricApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        await SetBearerTokenAsync(request, cancellationToken);

        using var client = _httpClientFactory.CreateClient(nameof(FabricPipelineService));
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Fabric pipeline trigger failed for pipeline {PipelineId}. Status: {StatusCode}",
                pipelineId, (int)response.StatusCode);
            throw new InvalidOperationException(
                $"Fabric pipeline trigger returned HTTP {(int)response.StatusCode} for pipeline '{pipelineId}'.");
        }

        // Fabric responds with 202 and a Location header pointing to the run instance.
        // The run ID is the last segment of that URL.
        var location = response.Headers.Location?.ToString();
        if (string.IsNullOrWhiteSpace(location))
        {
            _logger.LogError("Fabric pipeline trigger returned no Location header for pipeline {PipelineId}", pipelineId);
            throw new InvalidOperationException(
                $"Fabric pipeline trigger for '{pipelineId}' returned no Location header.");
        }

        var trimmedLocation = location.TrimEnd('/');
        var fabricRunId = trimmedLocation.Split('/').Last();
        if (string.IsNullOrEmpty(fabricRunId)) {
            _logger.LogError("Fabric Location header has no run ID segment for pipeline {PipelineId}: {Location}", pipelineId, location);
            throw new InvalidOperationException(
                $"Fabric pipeline trigger for '{pipelineId}' returned a Location header with no run ID segment.");
        }
        _logger.LogInformation(
            "Fabric pipeline {PipelineId} triggered. Run ID: {FabricRunId}",
            pipelineId, fabricRunId);

        return fabricRunId;
    }

    /// <inheritdoc/>
    public async Task<string> GetRunStatusAsync(
        string fabricRunId,
        string pipelineId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fabricRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineId);

        var url = $"{_config.WorkspaceEndpoint.TrimEnd('/')}/pipelines/{pipelineId}/jobs/instances/{fabricRunId}?api-version={FabricApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await SetBearerTokenAsync(request, cancellationToken);

        using var client = _httpClientFactory.CreateClient(nameof(FabricPipelineService));
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Fabric status check failed for run {FabricRunId}. Status: {StatusCode}",
                fabricRunId, (int)response.StatusCode);
            throw new InvalidOperationException(
                $"Fabric status check returned HTTP {(int)response.StatusCode} for run '{fabricRunId}'.");
        }

        using var jsonDoc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        // Fabric returns { "status": "Running" | "Succeeded" | "Failed" | "Cancelled" | ... }
        if (jsonDoc.RootElement.TryGetProperty("status", out var statusProp))
            return statusProp.GetString() ?? "Unknown";

        _logger.LogWarning("Fabric run status response missing 'status' field for run {FabricRunId}", fabricRunId);
        return "Unknown";
    }

    private async Task SetBearerTokenAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var tokenRequestContext = new TokenRequestContext([FabricTokenScope]);
        var accessToken = await _credential.GetTokenAsync(tokenRequestContext, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);
    }
}
