using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.Persistence.ExternalServices;

/// <summary>
/// Calls the local Python FastAPI processing service over HTTP to trigger and monitor pipeline runs.
/// No authentication is required — the local service runs without auth.
/// The local endpoint is read from <see cref="IngestionOptions.LocalEndpoint"/>.
/// </summary>
public sealed class LocalPipelineService : ILocalPipelineService {
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionOptions _config;
    private readonly BlobStorageOptions _blobStorageOptions;
    private readonly ILogger<LocalPipelineService> _logger;

    public LocalPipelineService(
        IHttpClientFactory httpClientFactory,
        IOptions<IngestionOptions> config,
        IOptions<BlobStorageOptions> blobStorageOptions,
        ILogger<LocalPipelineService> logger) {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(blobStorageOptions);
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(config.Value.LocalEndpoint))
            throw new InvalidOperationException(
                "Ingestion:LocalEndpoint is required.");

        _httpClientFactory = httpClientFactory;
        _config = config.Value;
        _blobStorageOptions = blobStorageOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> TriggerPipelineAsync(
        string uploadId,
        string documentType,
        string pipelineId,
        string? sourceAccessToken = null,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        if (string.IsNullOrWhiteSpace(sourceAccessToken)
            && documentType is "manual-pdf" or "spec-dataset") {
            throw new InvalidOperationException(
                "Source access token is required to trigger local ingestion.");
        }

        _logger.LogInformation(
            "Triggering local pipeline for document type {DocumentType}. SourceTokenPresent={HasToken}",
            LogSanitizer.Sanitize(documentType),
            !string.IsNullOrWhiteSpace(sourceAccessToken));

        var endpoint = documentType switch {
            "manual-pdf" => $"{_config.LocalEndpoint.TrimEnd('/')}/process/pdf",
            "spec-dataset" => $"{_config.LocalEndpoint.TrimEnd('/')}/process/csv",
            "bike-graph" => $"{_config.LocalEndpoint.TrimEnd('/')}/process/bike-graph",
            _ => throw new ArgumentException(
                $"Unsupported document type: '{documentType}'. Expected 'manual-pdf', 'spec-dataset', or 'bike-graph'.",
                nameof(documentType))
        };

        var blobContainer = _blobStorageOptions.RawUploadsContainer;

        var body = documentType switch {
            "bike-graph" => JsonSerializer.Serialize(new {
                upload_id = uploadId,
                blob_container = blobContainer,
                source_access_token = sourceAccessToken,
            }),
            _ => JsonSerializer.Serialize(new {
                upload_id = uploadId,
                document_type = documentType,
                blob_container = blobContainer,
                source_access_token = sourceAccessToken,
            })
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var client = _httpClientFactory.CreateClient("LocalPipelineService");
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode) {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(
                "Local pipeline trigger failed for document type {DocumentType}. Status: {StatusCode} Body: {Body}",
                LogSanitizer.Sanitize(documentType), (int)response.StatusCode, LogSanitizer.Sanitize(errorBody, 500));
            throw new InvalidOperationException(
                $"Local pipeline trigger returned HTTP {(int)response.StatusCode} for document type '{documentType}': {errorBody}");
        }

        using var jsonDoc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (jsonDoc.RootElement.TryGetProperty("job_id", out var jobIdProp)) {
            var jobId = jobIdProp.GetString();
            if (!string.IsNullOrWhiteSpace(jobId)) {
                _logger.LogInformation(
                    "Local pipeline triggered for document type {DocumentType}. Job ID: {JobId}",
                    LogSanitizer.Sanitize(documentType), LogSanitizer.Sanitize(jobId));
                return jobId;
            }
        }

        _logger.LogError(
            "Local pipeline trigger response missing 'job_id' field for document type {DocumentType}",
            LogSanitizer.Sanitize(documentType));
        throw new InvalidOperationException(
            $"Local pipeline trigger for document type '{documentType}' returned no job_id.");
    }

    /// <inheritdoc/>
    public async Task<PipelineRunStatusResult> GetRunStatusAsync(
        string runId,
        string pipelineId,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var url = $"{_config.LocalEndpoint.TrimEnd('/')}/jobs/{runId}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        using var client = _httpClientFactory.CreateClient("LocalPipelineService");
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "Local pipeline status check failed for job {RunId}. Status: {StatusCode} Body: {Body}",
                LogSanitizer.Sanitize(runId), (int)response.StatusCode, LogSanitizer.Sanitize(errorBody, 500));
            throw new InvalidOperationException(
                $"Local pipeline status check returned HTTP {(int)response.StatusCode} for job '{runId}': {errorBody}");
        }

        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        using var jsonDoc = JsonDocument.Parse(rawJson);

        return new PipelineRunStatusResult {
            Status = ReadJsonString(jsonDoc.RootElement, "status") ?? "Unknown",
            Message = ReadJsonString(jsonDoc.RootElement, "message"),
            Error = ReadJsonString(jsonDoc.RootElement, "error"),
            RawJson = rawJson,
        };
    }

    private static string? ReadJsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var prop) ? prop.GetString() : null;
}
