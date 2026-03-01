using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.ExternalServices;

/// <summary>
/// Calls the local Python FastAPI processing service over HTTP to trigger and monitor pipeline runs.
/// No authentication is required — the local service runs without auth.
/// The local endpoint is read from <see cref="IngestionOptions.LocalEndpoint"/>.
/// </summary>
public sealed class LocalPipelineService : ILocalPipelineService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionOptions _config;
    private readonly ILogger<LocalPipelineService> _logger;

    public LocalPipelineService(
        IHttpClientFactory httpClientFactory,
        IOptions<IngestionOptions> config,
        ILogger<LocalPipelineService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(config.Value.LocalEndpoint))
            throw new InvalidOperationException(
                "Ingestion:LocalEndpoint is required.");

        _httpClientFactory = httpClientFactory;
        _config = config.Value;
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

        _logger.LogInformation(
            "Triggering local pipeline for document type {DocumentType}",
            documentType);

        var endpoint = documentType switch
        {
            "manual-pdf" => $"{_config.LocalEndpoint.TrimEnd('/')}/process/pdf",
            "spec-dataset" => $"{_config.LocalEndpoint.TrimEnd('/')}/process/csv",
            _ => throw new ArgumentException(
                $"Unsupported document type: '{documentType}'. Expected 'manual-pdf' or 'spec-dataset'.",
                nameof(documentType))
        };

        var body = JsonSerializer.Serialize(new
        {
            upload_id = uploadId,
            document_type = documentType,
            blob_container = "raw-uploads"
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var client = _httpClientFactory.CreateClient(nameof(LocalPipelineService));
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Local pipeline trigger failed for document type {DocumentType}. Status: {StatusCode}",
                documentType, (int)response.StatusCode);
            throw new InvalidOperationException(
                $"Local pipeline trigger returned HTTP {(int)response.StatusCode} for document type '{documentType}'.");
        }

        using var jsonDoc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (jsonDoc.RootElement.TryGetProperty("job_id", out var jobIdProp))
        {
            var jobId = jobIdProp.GetString();
            if (!string.IsNullOrWhiteSpace(jobId))
            {
                _logger.LogInformation(
                    "Local pipeline triggered for document type {DocumentType}. Job ID: {JobId}",
                    documentType, jobId);
                return jobId;
            }
        }

        _logger.LogError(
            "Local pipeline trigger response missing 'job_id' field for document type {DocumentType}",
            documentType);
        throw new InvalidOperationException(
            $"Local pipeline trigger for document type '{documentType}' returned no job_id.");
    }

    /// <inheritdoc/>
    public async Task<string> GetRunStatusAsync(
        string runId,
        string pipelineId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var url = $"{_config.LocalEndpoint.TrimEnd('/')}/jobs/{runId}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        using var client = _httpClientFactory.CreateClient(nameof(LocalPipelineService));
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Local pipeline status check failed for job {RunId}. Status: {StatusCode}",
                runId, (int)response.StatusCode);
            throw new InvalidOperationException(
                $"Local pipeline status check returned HTTP {(int)response.StatusCode} for job '{runId}'.");
        }

        using var jsonDoc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (jsonDoc.RootElement.TryGetProperty("status", out var statusProp))
            return statusProp.GetString() ?? "Unknown";

        _logger.LogWarning("Local pipeline status response missing 'status' field for job {RunId}", runId);
        return "Unknown";
    }
}
