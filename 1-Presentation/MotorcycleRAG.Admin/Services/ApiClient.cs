using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MotorcycleRAG.Admin.Models.Api;
using MotorcycleRAG.Admin.Services.Dtos;
using MotorcycleRAG.Contracts.Models.DTOs;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// HTTP client wrapper for calling the Motorcycle RAG API.
/// Handles authentication, request/response serialization, and error handling.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by MAUI framework via DI")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S1200:Dependencies", Justification = "Resilience and DTOs")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1506:Coupling", Justification = "Inherent to resilience client")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "Internal access needed")]
internal class ApiClient {
    private readonly HttpClient _httpClient;
    private readonly IAdminAuthService _authService;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<ApiClient> _logger;

    public ApiClient(HttpClient httpClient, IAdminAuthService authService, ILogger<ApiClient> logger) {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>
    /// Ensures the HTTP client is configured with a base URL.
    /// Throws InvalidOperationException with a user-friendly message if not configured.
    /// </summary>
    private void EnsureConfigured() {
        if (_httpClient.BaseAddress == null) {
            throw new InvalidOperationException("API not configured. Go to Settings to configure the API base URL.");
        }
    }

    /// <summary>
    /// Ensures the HTTP client has a valid access token
    /// </summary>
    private async Task EnsureAuthenticatedAsync() {
        // First check that the API is configured
        EnsureConfigured();

        var accessToken = await _authService.GetAccessTokenAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken)) {
            throw new UnauthorizedAccessException("No valid access token available. Please sign in.");
        }

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    #region File Upload

    /// <summary>
    /// Uploads a single file to the pipeline
    /// </summary>
    internal Task<FileUploadResult> UploadFileAsync(string filePath) {
        return UploadFileAsync(filePath, processImmediately: false, default);
    }

    /// <summary>
    /// Uploads a single file to the pipeline with processing option
    /// </summary>
    internal Task<FileUploadResult> UploadFileAsync(string filePath, bool processImmediately) {
        return UploadFileAsync(filePath, processImmediately, default);
    }

    /// <summary>
    /// Uploads a single file to the pipeline with processing option and cancellation
    /// </summary>
    internal async Task<FileUploadResult> UploadFileAsync(string filePath, bool processImmediately, CancellationToken cancellationToken) {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        try {
            using var fileStream = File.OpenRead(filePath);
            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));

            content.Add(streamContent, "file", Path.GetFileName(filePath));

            var requestPath = processImmediately
                ? "api/file-upload/with-processing?processImmediately=true"
                : "api/file-upload";
            var requestUri = new Uri(requestPath, UriKind.Relative);
            var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(
                requestUri,
                content,
                cancellationToken)).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
                throw new UnauthorizedAccessException(
                    $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
            }
            response.EnsureSuccessStatusCode();

            var result = processImmediately
                ? (await response.Content.ReadFromJsonAsync<FileUploadResponseDto>(_jsonOptions, cancellationToken).ConfigureAwait(false))?.Upload
                : await response.Content.ReadFromJsonAsync<FileUploadResult>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            UploadResultValidator.ValidateFileUploadResult(result);
            return result!;
        }
        catch (HttpRequestException ex) {
            _logger.LogError(ex, "HTTP request failed uploading file: {FileName}. Status: {StatusCode}",
                Path.GetFileName(filePath), ex.StatusCode);
            throw new FileLoadException($"HTTP request failed uploading file: {Path.GetFileName(filePath)}. Status: {ex.StatusCode}", ex);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Unexpected error uploading file: {FileName}", Path.GetFileName(filePath));
            throw new FileLoadException($"Unexpected error uploading file: {Path.GetFileName(filePath)}", ex);
        }
    }

    /// <summary>
    /// Uploads multiple files to the pipeline
    /// </summary>
    internal Task<BatchFileUploadResult> UploadBatchAsync(IEnumerable<string> filePaths) {
        return UploadBatchAsync(filePaths, processImmediately: false, default);
    }

    /// <summary>
    /// Uploads multiple files to the pipeline with processing option
    /// </summary>
    internal Task<BatchFileUploadResult> UploadBatchAsync(IEnumerable<string> filePaths, bool processImmediately) {
        return UploadBatchAsync(filePaths, processImmediately, default);
    }

    /// <summary>
    /// Uploads multiple files to the pipeline with processing option and cancellation
    /// </summary>
    public async Task<BatchFileUploadResult> UploadBatchAsync(IEnumerable<string> filePaths, bool processImmediately, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(filePaths);
        var filePathList = filePaths.ToArray();
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var streams = new List<FileStream>();
        var streamContents = new List<StreamContent>();

        try {
            using var content = new MultipartFormDataContent();

            foreach (var filePath in filePathList) {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {filePath}");

                var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                streams.Add(fileStream);

                var streamContent = new StreamContent(fileStream);
                streamContents.Add(streamContent);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
                content.Add(streamContent, "files", Path.GetFileName(filePath));
            }

            var requestPath = processImmediately
                ? "api/file-upload/batch-with-processing?processImmediately=true"
                : "api/file-upload/batch";
            var requestUri = new Uri(requestPath, UriKind.Relative);
            var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(
                requestUri,
                content,
                cancellationToken)).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
                throw new UnauthorizedAccessException(
                    $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
            }
            response.EnsureSuccessStatusCode();

            var batchResult = processImmediately
                ? (await response.Content.ReadFromJsonAsync<BatchFileUploadResponseDto>(_jsonOptions, cancellationToken).ConfigureAwait(false))?.Upload
                : await response.Content.ReadFromJsonAsync<BatchFileUploadResult>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            UploadResultValidator.ValidateBatchFileUploadResult(batchResult);
            return batchResult!;
        }
        catch (HttpRequestException ex) {
            _logger.LogError(ex, "HTTP request failed uploading batch files. Status: {StatusCode}, File count: {FileCount}",
                ex.StatusCode, filePathList.Length);
            throw new FileLoadException($"HTTP error uploading batch files. Status: {ex.StatusCode}. File paths: {string.Join(", ", filePathList)}", ex);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Unexpected error uploading batch files. File count: {FileCount}", filePathList.Length);
            throw new FileLoadException($"Error uploading batch files. See inner exception for details. File paths: {string.Join(", ", filePathList)}", ex);
        }
        finally {
            foreach (var streamContent in streamContents) {
                streamContent?.Dispose();
            }

            foreach (var stream in streams.Where(s => s != null)) {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Gets upload constraints (file size limits, allowed types, etc.)
    /// </summary>
    internal Task<UploadConstraints> GetUploadConstraintsAsync() {
        return GetUploadConstraintsAsync(default);
    }

    /// <summary>
    /// Gets upload constraints (file size limits, allowed types, etc.) with cancellation
    /// </summary>
    internal async Task<UploadConstraints> GetUploadConstraintsAsync(CancellationToken cancellationToken) {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var requestUri = new Uri("api/datapipeline/upload-constraints", UriKind.Relative);
        var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UploadConstraints>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize constraints response");
    }

    #endregion

    #region Pipeline Processing

    /// <summary>
    /// Processes a previously uploaded file
    /// </summary>
    internal Task<ProcessingResult> ProcessFileAsync(string executionId) {
        return ProcessFileAsync(executionId, default);
    }

    /// <summary>
    /// Processes a previously uploaded file with cancellation
    /// </summary>
    internal async Task<ProcessingResult> ProcessFileAsync(string executionId, CancellationToken cancellationToken) {
        ValidateExecutionId(executionId);
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var requestUri = new Uri($"api/datapipeline/process/{Uri.EscapeDataString(executionId)}", UriKind.Relative);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(
            requestUri,
            null,
            cancellationToken)).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ProcessingResult>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize processing response");
    }

    /// <summary>
    /// Gets the status of a pipeline execution
    /// </summary>
    internal Task<PipelineStatusResponse> GetPipelineStatusAsync(string executionId) {
        return GetPipelineStatusAsync(executionId, default);
    }

    /// <summary>
    /// Gets the status of a pipeline execution with cancellation
    /// </summary>
    internal async Task<PipelineStatusResponse> GetPipelineStatusAsync(string executionId, CancellationToken cancellationToken) {
        ValidateExecutionId(executionId);
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var requestUri = new Uri($"api/datapipeline/status/{Uri.EscapeDataString(executionId)}", UriKind.Relative);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(requestUri, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PipelineStatusResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize status response");
    }

    /// <summary>
    /// Gets metrics for a pipeline execution
    /// </summary>
    internal Task<PipelineMetrics> GetPipelineMetricsAsync(string executionId) {
        return GetPipelineMetricsAsync(executionId, default);
    }

    /// <summary>
    /// Gets metrics for a pipeline execution with cancellation
    /// </summary>
    internal async Task<PipelineMetrics> GetPipelineMetricsAsync(string executionId, CancellationToken cancellationToken) {
        ValidateExecutionId(executionId);
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var relativePath = $"api/datapipeline/metrics/{Uri.EscapeDataString(executionId)}";
        var uri = new Uri(_httpClient.BaseAddress!, relativePath);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(uri, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PipelineMetrics>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize metrics response");
    }

    /// <summary>
    /// Cancels a running pipeline execution
    /// </summary>
    internal Task<CancelPipelineResponse> CancelPipelineAsync(string executionId) {
        return CancelPipelineAsync(executionId, default);
    }

    /// <summary>
    /// Cancels a running pipeline execution with cancellation
    /// </summary>
    internal async Task<CancelPipelineResponse> CancelPipelineAsync(string executionId, CancellationToken cancellationToken) {
        ValidateExecutionId(executionId);
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var relativePath = $"api/pipeline-processing/cancel/{Uri.EscapeDataString(executionId)}";
        var uri = new Uri(_httpClient.BaseAddress!, relativePath);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(uri, null, cancellationToken)).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<CancelPipelineResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize cancel response");
    }

    /// <summary>
    /// Gets all pipeline executions with optional filtering
    /// </summary>
    internal Task<List<PipelineExecution>> GetPipelineExecutionsAsync(CancellationToken cancellationToken) {
        return GetPipelineExecutionsAsync(status: null, startTime: null, endTime: null, cancellationToken);
    }

    /// <summary>
    /// Gets all pipeline executions with optional filtering
    /// </summary>
    internal Task<List<PipelineExecution>> GetPipelineExecutionsAsync(PipelineStatus? status, CancellationToken cancellationToken) {
        return GetPipelineExecutionsAsync(status, startTime: null, endTime: null, cancellationToken);
    }

    /// <summary>
    /// Gets all pipeline executions with optional filtering
    /// </summary>
    internal async Task<List<PipelineExecution>> GetPipelineExecutionsAsync(
        PipelineStatus? status,
        DateTime? startTime,
        DateTime? endTime,
        CancellationToken cancellationToken) {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var requestUri = new Uri(_httpClient.BaseAddress!, "api/pipeline-processing/metrics/168");
        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(requestUri, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();

        var metrics = await response.Content.ReadFromJsonAsync<PipelineMetrics>(_jsonOptions, cancellationToken).ConfigureAwait(false)
                      ?? new PipelineMetrics();

        IEnumerable<PipelineExecutionSummary> executions = metrics.RecentExecutions;

        if (status.HasValue) {
            executions = executions.Where(execution => execution.Status == status.Value);
        }

        if (startTime.HasValue) {
            executions = executions.Where(execution => execution.StartTime >= startTime.Value);
        }

        if (endTime.HasValue) {
            executions = executions.Where(execution => execution.StartTime <= endTime.Value);
        }

        return executions
            .OrderByDescending(execution => execution.StartTime)
            .Select(MapPipelineExecution)
            .ToList();
    }

    /// <summary>
    /// Gets blob-backed source files that have not completed ingestion yet.
    /// </summary>
    internal async Task<List<PendingStorageFileDto>> GetPendingStorageFilesAsync(CancellationToken cancellationToken) {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var requestUri = new Uri(_httpClient.BaseAddress!, "api/ingestion/jobs/pending-files");
        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(requestUri, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<PendingStorageFileDto>>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? new List<PendingStorageFileDto>();
    }

    /// <summary>
    /// Deletes a pending storage file from the queue.
    /// </summary>
    internal Task DeletePendingStorageFileAsync(string uploadId) {
        return DeletePendingStorageFileAsync(uploadId, default);
    }

    /// <summary>
    /// Deletes a pending storage file from the queue with cancellation.
    /// </summary>
    internal async Task DeletePendingStorageFileAsync(string uploadId, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(uploadId)) {
            throw new ArgumentException("Upload ID cannot be null or empty.", nameof(uploadId));
        }

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var relativePath = $"api/ingestion/jobs/pending-files/{Uri.EscapeDataString(uploadId)}";
        var uri = new Uri(_httpClient.BaseAddress!, relativePath);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.DeleteAsync(uri, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Clears all pending storage files from the queue.
    /// </summary>
    internal Task ClearPendingStorageFilesAsync() {
        return ClearPendingStorageFilesAsync(default);
    }

    /// <summary>
    /// Clears all pending storage files from the queue with cancellation.
    /// </summary>
    internal async Task ClearPendingStorageFilesAsync(CancellationToken cancellationToken) {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var requestUri = new Uri(_httpClient.BaseAddress!, "api/ingestion/jobs/pending-files");
        var response = await ExecuteWithResilienceAsync(() => _httpClient.DeleteAsync(requestUri, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Gets recent ingestion jobs for the admin status view.
    /// </summary>
    internal async Task<List<IngestionJobStatusResponse>> GetIngestionJobsAsync(int top, CancellationToken cancellationToken) {
        if (top <= 0)
            throw new ArgumentOutOfRangeException(nameof(top), "top must be greater than zero.");

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var requestUri = new Uri(_httpClient.BaseAddress!, $"api/ingestion/jobs?top={top}");
        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(requestUri, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<IngestionJobStatusResponse>>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? new List<IngestionJobStatusResponse>();
    }

    /// <summary>
    /// Deletes a terminal ingestion job from history.
    /// </summary>
    internal Task DeleteIngestionJobAsync(Guid jobId) {
        return DeleteIngestionJobAsync(jobId, default);
    }

    /// <summary>
    /// Deletes a terminal ingestion job from history with cancellation.
    /// </summary>
    internal async Task DeleteIngestionJobAsync(Guid jobId, CancellationToken cancellationToken) {
        if (jobId == Guid.Empty) {
            throw new ArgumentException("Job ID cannot be empty.", nameof(jobId));
        }

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var relativePath = $"api/ingestion/jobs/{Uri.EscapeDataString(jobId.ToString())}";
        var uri = new Uri(_httpClient.BaseAddress!, relativePath);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.DeleteAsync(uri, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Clears failed terminal ingestion jobs.
    /// </summary>
    internal Task ClearFailedIngestionJobsAsync() {
        return ClearFailedIngestionJobsAsync(default);
    }

    /// <summary>
    /// Clears failed terminal ingestion jobs with cancellation.
    /// </summary>
    internal async Task ClearFailedIngestionJobsAsync(CancellationToken cancellationToken) {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var requestUri = new Uri(_httpClient.BaseAddress!, "api/ingestion/jobs/failed");
        var response = await ExecuteWithResilienceAsync(() => _httpClient.DeleteAsync(requestUri, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Clears finished terminal ingestion jobs.
    /// </summary>
    internal Task ClearFinishedIngestionJobsAsync() {
        return ClearFinishedIngestionJobsAsync(default);
    }

    /// <summary>
    /// Clears finished terminal ingestion jobs with cancellation.
    /// </summary>
    internal async Task ClearFinishedIngestionJobsAsync(CancellationToken cancellationToken) {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var requestUri = new Uri(_httpClient.BaseAddress!, "api/ingestion/jobs/finished");
        var response = await ExecuteWithResilienceAsync(() => _httpClient.DeleteAsync(requestUri, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Retries a failed terminal ingestion job.
    /// </summary>
    internal Task<IngestionJobStatusResponse> RetryIngestionJobAsync(Guid jobId) {
        return RetryIngestionJobAsync(jobId, default);
    }

    /// <summary>
    /// Retries a failed terminal ingestion job with cancellation.
    /// </summary>
    internal async Task<IngestionJobStatusResponse> RetryIngestionJobAsync(Guid jobId, CancellationToken cancellationToken) {
        if (jobId == Guid.Empty) {
            throw new ArgumentException("Job ID cannot be empty.", nameof(jobId));
        }

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var relativePath = $"api/ingestion/jobs/{Uri.EscapeDataString(jobId.ToString())}/retry";
        var uri = new Uri(_httpClient.BaseAddress!, relativePath);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(uri, null, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<IngestionJobStatusResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize retry response");
    }

    /// <summary>
    /// Starts an ingestion job for a blob-backed source file.
    /// </summary>
    internal async Task<IngestionJobStatusResponse> StartIngestionJobAsync(
        IngestionJobStartRequest request,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var response = await ExecuteWithResilienceAsync(() =>
            _httpClient.PostAsJsonAsync("api/ingestion/jobs", request, _jsonOptions, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<IngestionJobStatusResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize ingestion job response");
    }

    /// <summary>
    /// Imports graph entities that were already produced by the local processor.
    /// </summary>
    internal async Task<IngestionJobStatusResponse> ImportGraphArtifactsAsync(
        GraphImportStartRequest request,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var response = await ExecuteWithResilienceAsync(() =>
            _httpClient.PostAsJsonAsync("api/ingestion/jobs/graph-import", request, _jsonOptions, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<IngestionJobStatusResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize graph import response");
    }

    /// <summary>
    /// Uploads a local ingestion source file to the API-managed raw storage area.
    /// </summary>
    internal async Task<IngestionUploadResponse> UploadIngestionSourceAsync(
        string filePath,
        string documentType,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        if (string.IsNullOrWhiteSpace(documentType))
            throw new ArgumentException("Document type cannot be null or empty.", nameof(documentType));

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        try {
            using var fileStream = File.OpenRead(filePath);
            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
            content.Add(streamContent, "file", Path.GetFileName(filePath));

            var requestUri = new Uri(
                _httpClient.BaseAddress!,
                $"api/ingestion/jobs/upload?documentType={Uri.EscapeDataString(documentType)}");

            var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(
                requestUri,
                content,
                cancellationToken)).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
                throw new UnauthorizedAccessException(
                    $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<IngestionUploadResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("Failed to deserialize ingestion upload response");
        }
        catch (HttpRequestException ex) {
            _logger.LogError(ex, "HTTP request failed uploading ingestion source {FileName}. Status: {StatusCode}",
                Path.GetFileName(filePath), ex.StatusCode);
            throw;
        }
    }

    private async Task<HttpResponseMessage> ExecuteWithResilienceAsync(Func<Task<HttpResponseMessage>> action) {
        return await action().ConfigureAwait(false);
    }

    #endregion

    #region MCP Tools

    /// <summary>
    /// Gets all MCP tool configurations
    /// </summary>
    internal Task<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto[]> GetMcpToolsAsync() {
        return GetMcpToolsAsync(default);
    }

    /// <summary>
    /// Gets all MCP tool configurations with cancellation
    /// </summary>
    internal async Task<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto[]> GetMcpToolsAsync(CancellationToken cancellationToken) {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var uri = new Uri(_httpClient.BaseAddress!, "api/admin/mcp-tools");
        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(uri, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto[]>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? Array.Empty<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto>();
    }

    /// <summary>
    /// Updates an MCP tool configuration
    /// </summary>
    internal Task<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto> UpdateMcpToolAsync(
        string toolId,
        MotorcycleRAG.Admin.Services.Dtos.UpdateMcpToolRequest request) {
        return UpdateMcpToolAsync(toolId, request, default);
    }

    /// <summary>
    /// Updates an MCP tool configuration with cancellation
    /// </summary>
    internal async Task<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto> UpdateMcpToolAsync(
        string toolId,
        MotorcycleRAG.Admin.Services.Dtos.UpdateMcpToolRequest request,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(toolId))
            throw new ArgumentException("Tool ID cannot be null or empty", nameof(toolId));

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var response = await ExecuteWithResilienceAsync(() =>
            _httpClient.PutAsJsonAsync(
                $"api/admin/mcp-tools/{Uri.EscapeDataString(toolId)}",
                request,
                _jsonOptions,
                cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize MCP tool response");
    }

    #endregion

    #region Web Sources

    /// <summary>
    /// Gets all web sources
    /// </summary>
    internal Task<List<WebSource>> GetWebSourcesAsync() {
        return GetWebSourcesAsync(default);
    }

    /// <summary>
    /// Gets all web sources with cancellation
    /// </summary>
    internal async Task<List<WebSource>> GetWebSourcesAsync(CancellationToken cancellationToken) {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var uri = new Uri(_httpClient.BaseAddress!, "api/admin/web-sources");
        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(uri, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<WebSource>>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? new List<WebSource>();
    }

    /// <summary>
    /// Adds a new web source
    /// </summary>
    internal Task<WebSource> AddWebSourceAsync(WebSource source) {
        return AddWebSourceAsync(source, default);
    }

    /// <summary>
    /// Adds a new web source with cancellation
    /// </summary>
    internal async Task<WebSource> AddWebSourceAsync(WebSource source, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(source);

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var response = await ExecuteWithResilienceAsync(() =>
            _httpClient.PostAsJsonAsync("api/admin/web-sources", source, _jsonOptions, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<WebSource>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize web source response");
    }

    /// <summary>
    /// Updates an existing web source
    /// </summary>
    internal Task<WebSource> UpdateWebSourceAsync(WebSource source) {
        return UpdateWebSourceAsync(source, default);
    }

    /// <summary>
    /// Updates an existing web source with cancellation
    /// </summary>
    internal async Task<WebSource> UpdateWebSourceAsync(WebSource source, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Id <= 0)
            throw new ArgumentException("Invalid web source ID", nameof(source));

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var response = await ExecuteWithResilienceAsync(() =>
            _httpClient.PutAsJsonAsync($"api/admin/web-sources/{source.Id}", source, _jsonOptions, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<WebSource>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize web source response");
    }

    /// <summary>
    /// Deletes a web source
    /// </summary>
    internal Task DeleteWebSourceAsync(int sourceId) {
        return DeleteWebSourceAsync(sourceId, default);
    }

    /// <summary>
    /// Deletes a web source with cancellation
    /// </summary>
    internal async Task DeleteWebSourceAsync(int sourceId, CancellationToken cancellationToken) {
        if (sourceId <= 0)
            throw new ArgumentException("Invalid web source ID", nameof(sourceId));

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var relativePath = $"api/admin/web-sources/{sourceId}";
        var uri = new Uri(_httpClient.BaseAddress!, relativePath);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.DeleteAsync(uri, cancellationToken)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
        response.EnsureSuccessStatusCode();
    }

    #endregion

    #region User Management

    internal Task<UserManagementListResponseDto> GetUserManagementAsync(
        UserManagementRowState? rowState = null,
        string? search = null,
        int page = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default) {
        return GetUserManagementInternalAsync(rowState, search, page, pageSize, cancellationToken);
    }

    internal Task<UserManagementRowDto> ApproveAccessRequestAsync(
        string requestId,
        ApproveAccessRequestDto request,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);
        ValidateManagementIdentifier(requestId, nameof(requestId));

        return ExecuteAdminActionAsync(
            $"api/admin/access-requests/{Uri.EscapeDataString(requestId)}/approve",
            request,
            cancellationToken);
    }

    internal Task<UserManagementRowDto> RetryAccessRequestOnboardingAsync(
        string requestId,
        RetryOnboardingDto request,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);
        ValidateManagementIdentifier(requestId, nameof(requestId));

        return ExecuteAdminActionAsync(
            $"api/admin/access-requests/{Uri.EscapeDataString(requestId)}/retry-onboarding",
            request,
            cancellationToken);
    }

    internal Task<UserManagementRowDto> ChangeManagedUserTierAsync(
        string userId,
        ChangeManagedUserTierDto request,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);
        ValidateManagementIdentifier(userId, nameof(userId));

        return ExecuteAdminActionAsync(
            $"api/admin/users/{Uri.EscapeDataString(userId)}/change-tier",
            request,
            cancellationToken);
    }

    internal Task<UserManagementRowDto> CancelAccessRequestAsync(
        string requestId,
        CancelManagementItemDto request,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);
        ValidateManagementIdentifier(requestId, nameof(requestId));

        return ExecuteAdminActionAsync(
            $"api/admin/access-requests/{Uri.EscapeDataString(requestId)}/cancel",
            request,
            cancellationToken);
    }

    internal Task<UserManagementRowDto> CancelManagedUserAsync(
        string userId,
        CancelManagementItemDto request,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);
        ValidateManagementIdentifier(userId, nameof(userId));

        return ExecuteAdminActionAsync(
            $"api/admin/users/{Uri.EscapeDataString(userId)}/cancel",
            request,
            cancellationToken);
    }

    #endregion

    #region User Administration

    /// <summary>
    /// Gets all users with optional filtering
    /// </summary>
    internal Task<List<UserDto>> GetUsersAsync(CancellationToken cancellationToken) {
        return GetUsersAsync(isEnabled: null, cancellationToken);
    }

    /// <summary>
    /// Gets all users with optional filtering
    /// </summary>
    internal async Task<List<UserDto>> GetUsersAsync(bool? isEnabled, CancellationToken cancellationToken) {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        const int pageSize = 100;
        var page = 1;
        var users = new List<UserDto>();
        var totalCount = int.MaxValue;

        while (users.Count < totalCount) {
            var relativePath = $"api/admin/users?page={page}&pageSize={pageSize}";
            var uri = new Uri(_httpClient.BaseAddress!, relativePath);
            var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(uri, cancellationToken)).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
                throw new UnauthorizedAccessException(
                    $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
            }

            response.EnsureSuccessStatusCode();

            var pageResult = await response.Content.ReadFromJsonAsync<UserListResponseDto>(_jsonOptions, cancellationToken).ConfigureAwait(false)
                             ?? new UserListResponseDto();

            users.AddRange(pageResult.Users);
            totalCount = pageResult.TotalCount;
            page++;

            if (pageResult.Users.Count == 0) {
                break;
            }
        }

        return isEnabled.HasValue
            ? users.Where(user => user.IsEnabled == isEnabled.Value).ToList()
            : users;
    }

    /// <summary>
    /// Enables a user account
    /// </summary>
    internal Task<UserDto> EnableUserAsync(string userId) {
        return EnableUserAsync(userId, default);
    }

    /// <summary>
    /// Enables a user account with cancellation
    /// </summary>
    internal async Task<UserDto> EnableUserAsync(string userId, CancellationToken cancellationToken) {
        return await SetUserEnabledAsync(userId, isEnabled: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Disables a user account
    /// </summary>
    internal Task<UserDto> DisableUserAsync(string userId) {
        return DisableUserAsync(userId, default);
    }

    /// <summary>
    /// Disables a user account with cancellation
    /// </summary>
    internal async Task<UserDto> DisableUserAsync(string userId, CancellationToken cancellationToken) {
        return await SetUserEnabledAsync(userId, isEnabled: false, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Helpers

    private async Task<UserManagementListResponseDto> GetUserManagementInternalAsync(
        UserManagementRowState? rowState,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken) {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page), page, "Page must be at least 1.");

        if (pageSize < 1)
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be at least 1.");

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var relativePath = BuildUserManagementQuery(rowState, search, page, pageSize);
        var uri = new Uri(_httpClient.BaseAddress!, relativePath);
        var response = await ExecuteWithResilienceAsync(() =>
            _httpClient.GetAsync(uri, cancellationToken)).ConfigureAwait(false);
        EnsureAuthorizedResponse(response);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UserManagementListResponseDto>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? new UserManagementListResponseDto();
    }

    private async Task<UserManagementRowDto> ExecuteAdminActionAsync<TRequest>(
        string relativePath,
        TRequest request,
        CancellationToken cancellationToken) {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var response = await ExecuteWithResilienceAsync(() =>
            _httpClient.PostAsJsonAsync(relativePath, request, _jsonOptions, cancellationToken)).ConfigureAwait(false);
        EnsureAuthorizedResponse(response);
        if (!response.IsSuccessStatusCode) {
            var message = await ReadAdminActionErrorMessageAsync(response, cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(message, null, response.StatusCode);
        }

        var actionResponse = await response.Content.ReadFromJsonAsync<AdminActionResponseDto>(_jsonOptions, cancellationToken).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Failed to deserialize admin action response");

        return actionResponse.Row;
    }

    private static async Task<string> ReadAdminActionErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        if (response.Content is null) {
            return CreateAdminActionFallbackMessage(response);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content)) {
            return CreateAdminActionFallbackMessage(response);
        }

        try {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            if (TryReadJsonString(root, "error", out var error) ||
                TryReadJsonString(root, "message", out error) ||
                TryReadJsonString(root, "title", out error)) {
                return error;
            }
        }
        catch (JsonException) {
            // Fall through to the plain-text response body.
        }

        return content;
    }

    private static bool TryReadJsonString(JsonElement element, string propertyName, out string value) {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String) {
            return false;
        }

        var propertyValue = property.GetString();
        if (string.IsNullOrWhiteSpace(propertyValue)) {
            return false;
        }

        value = propertyValue;
        return true;
    }

    private static string CreateAdminActionFallbackMessage(HttpResponseMessage response) {
        return !string.IsNullOrWhiteSpace(response.ReasonPhrase)
            ? response.ReasonPhrase
            : $"Admin action failed with status code {(int)response.StatusCode}.";
    }

    private static void ValidateManagementIdentifier(string identifier, string paramName) {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be null or empty", paramName);
    }

    private static void EnsureAuthorizedResponse(HttpResponseMessage response) {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
            throw new UnauthorizedAccessException(
                $"Access denied ({(int)response.StatusCode}). Please check your permissions and try signing in again.");
        }
    }

    private static string BuildUserManagementQuery(
        UserManagementRowState? rowState,
        string? search,
        int page,
        int pageSize) {
        var query = new StringBuilder("api/admin/user-management?");
        query.Append($"page={page}&pageSize={pageSize}");

        if (rowState.HasValue) {
            query.Append("&rowState=");
            query.Append(Uri.EscapeDataString(rowState.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(search)) {
            query.Append("&search=");
            query.Append(Uri.EscapeDataString(search));
        }

        return query.ToString();
    }

    private async Task<UserDto> SetUserEnabledAsync(string userId, bool isEnabled, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var request = new SetUserEnabledRequestDto { IsEnabled = isEnabled };
        var relativePath = $"api/admin/users/{Uri.EscapeDataString(userId)}/enabled";
        var response = await ExecuteWithResilienceAsync(() =>
            _httpClient.PutAsJsonAsync(relativePath, request, _jsonOptions, cancellationToken)).ConfigureAwait(false);
        EnsureAuthorizedResponse(response);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UserDto>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize user response");
    }

    /// <summary>
    /// Validates that executionId is in a valid GUID format
    /// </summary>
    private static void ValidateExecutionId(string executionId) {
        if (string.IsNullOrWhiteSpace(executionId))
            throw new ArgumentException("Execution ID cannot be null or empty", nameof(executionId));

        if (!Guid.TryParse(executionId, out _))
            throw new ArgumentException($"Invalid execution ID format. Expected valid GUID, got: {executionId}", nameof(executionId));
    }

    private static PipelineExecution MapPipelineExecution(PipelineExecutionSummary executionSummary) {
        var execution = new PipelineExecution {
            ExecutionId = executionSummary.ExecutionId,
            PipelineType = executionSummary.FileType.ToString(),
            Status = executionSummary.Status,
            StartTime = executionSummary.StartTime,
            EndTime = executionSummary.Duration > TimeSpan.Zero
                ? executionSummary.StartTime.Add(executionSummary.Duration)
                : null
        };

        if (!string.IsNullOrWhiteSpace(executionSummary.ErrorMessage)) {
            execution.Errors.Add(executionSummary.ErrorMessage);
        }

        return execution;
    }

    /// <summary>
    /// Gets the MIME content type for a file based on its extension
    /// </summary>
    private static string GetContentType(string filePath) {
        var extension = Path.GetExtension(filePath).ToUpperInvariant();
        return extension switch {
            ".PDF" => "application/pdf",
            ".CSV" => "text/csv",
            ".JSON" => "application/json",
            ".TXT" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    #endregion
}
