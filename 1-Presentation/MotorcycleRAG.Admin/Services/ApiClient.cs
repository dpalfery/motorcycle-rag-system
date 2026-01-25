using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MotorcycleRAG.Admin.Models.Api;
using MotorcycleRAG.Admin.Services.Dtos;
using MotorcycleRAG.Contracts.Models.DTOs;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;
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
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private readonly AsyncCircuitBreakerPolicy<HttpResponseMessage> _circuitBreaker;
    private readonly ILogger<ApiClient> _logger;

    public ApiClient(HttpClient httpClient, IAdminAuthService authService, ILogger<ApiClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .OrResult<HttpResponseMessage>(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                                                (int)r.StatusCode == 503)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (outcome, timespan, retryCount, _) => {
                    _logger.LogWarning("ApiClient retry {Retry} after {Delay}s. Reason: {Reason}",
                        retryCount, timespan.TotalSeconds, outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString());
                });

        _circuitBreaker = Policy
            .Handle<HttpRequestException>()
            .OrResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
            .CircuitBreakerAsync(5, TimeSpan.FromMinutes(1),
                onBreak: (outcome, ts) => _logger.LogWarning("ApiClient circuit opened for {Duration}s. Reason: {Reason}",
                    ts.TotalSeconds, outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()),
                onReset: () => _logger.LogInformation("ApiClient circuit reset"),
                onHalfOpen: () => _logger.LogInformation("ApiClient circuit half-open"));
    }

    /// <summary>
    /// Ensures the HTTP client is configured with a base URL.
    /// Throws InvalidOperationException with a user-friendly message if not configured.
    /// </summary>
    private void EnsureConfigured()
    {
        if (_httpClient.BaseAddress == null)
        {
            throw new InvalidOperationException("API not configured. Go to Settings to configure the API base URL.");
        }
    }

    /// <summary>
    /// Ensures the HTTP client has a valid access token
    /// </summary>
    private async Task EnsureAuthenticatedAsync()
    {
        // First check that the API is configured
        EnsureConfigured();

        var accessToken = await _authService.GetAccessTokenAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new UnauthorizedAccessException("No valid access token available. Please sign in.");
        }

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    #region File Upload

    /// <summary>
    /// Uploads a single file to the pipeline
    /// </summary>
    internal Task<FileUploadResult> UploadFileAsync(string filePath)
    {
        return UploadFileAsync(filePath, processImmediately: false, default);
    }

    /// <summary>
    /// Uploads a single file to the pipeline with processing option
    /// </summary>
    internal Task<FileUploadResult> UploadFileAsync(string filePath, bool processImmediately)
    {
        return UploadFileAsync(filePath, processImmediately, default);
    }

    /// <summary>
    /// Uploads a single file to the pipeline with processing option and cancellation
    /// </summary>
    internal async Task<FileUploadResult> UploadFileAsync(string filePath, bool processImmediately, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));

            content.Add(streamContent, "file", Path.GetFileName(filePath));

            var requestUri = new Uri($"api/datapipeline/upload?processImmediately={processImmediately}", UriKind.Relative);
            var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(
                requestUri,
                content,
                cancellationToken)).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<FileUploadResult>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            UploadResultValidator.ValidateFileUploadResult(result);
            return result!;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed uploading file: {FileName}. Status: {StatusCode}",
                Path.GetFileName(filePath), ex.StatusCode);
            throw new FileLoadException($"HTTP request failed uploading file: {Path.GetFileName(filePath)}. Status: {ex.StatusCode}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error uploading file: {FileName}", Path.GetFileName(filePath));
            throw new FileLoadException($"Unexpected error uploading file: {Path.GetFileName(filePath)}", ex);
        }
    }

    /// <summary>
    /// Uploads multiple files to the pipeline
    /// </summary>
    internal Task<BatchFileUploadResult> UploadBatchAsync(IEnumerable<string> filePaths)
    {
        return UploadBatchAsync(filePaths, processImmediately: false, default);
    }

    /// <summary>
    /// Uploads multiple files to the pipeline with processing option
    /// </summary>
    internal Task<BatchFileUploadResult> UploadBatchAsync(IEnumerable<string> filePaths, bool processImmediately)
    {
        return UploadBatchAsync(filePaths, processImmediately, default);
    }

    /// <summary>
    /// Uploads multiple files to the pipeline with processing option and cancellation
    /// </summary>
    public async Task<BatchFileUploadResult> UploadBatchAsync(IEnumerable<string> filePaths, bool processImmediately, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        var filePathList = filePaths.ToArray();
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var streams = new List<FileStream>();
        var streamContents = new List<StreamContent>();

        try
        {
            using var content = new MultipartFormDataContent();

            foreach (var filePath in filePathList)
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {filePath}");

                var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                streams.Add(fileStream);

                var streamContent = new StreamContent(fileStream);
                streamContents.Add(streamContent);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
                content.Add(streamContent, "files", Path.GetFileName(filePath));
            }

            var requestUri = new Uri($"api/datapipeline/upload-batch?processImmediately={processImmediately}", UriKind.Relative);
            var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(
                requestUri,
                content,
                cancellationToken)).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var batchResult = await response.Content.ReadFromJsonAsync<BatchFileUploadResult>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            UploadResultValidator.ValidateBatchFileUploadResult(batchResult);
            return batchResult!;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed uploading batch files. Status: {StatusCode}, File count: {FileCount}",
                ex.StatusCode, filePathList.Length);
            throw new FileLoadException($"HTTP error uploading batch files. Status: {ex.StatusCode}. File paths: {string.Join(", ", filePathList)}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error uploading batch files. File count: {FileCount}", filePathList.Length);
            throw new FileLoadException($"Error uploading batch files. See inner exception for details. File paths: {string.Join(", ", filePathList)}", ex);
        }
        finally
        {
            foreach (var streamContent in streamContents)
            {
                streamContent?.Dispose();
            }

            foreach (var stream in streams.Where(s => s != null))
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Gets upload constraints (file size limits, allowed types, etc.)
    /// </summary>
    internal Task<UploadConstraints> GetUploadConstraintsAsync()
    {
        return GetUploadConstraintsAsync(default);
    }

    /// <summary>
    /// Gets upload constraints (file size limits, allowed types, etc.) with cancellation
    /// </summary>
    internal async Task<UploadConstraints> GetUploadConstraintsAsync(CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var requestUri = new Uri("api/datapipeline/upload-constraints", UriKind.Relative);
        var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UploadConstraints>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize constraints response");
    }

    #endregion

    #region Pipeline Processing

    /// <summary>
    /// Processes a previously uploaded file
    /// </summary>
    internal Task<ProcessingResult> ProcessFileAsync(string executionId)
    {
        return ProcessFileAsync(executionId, default);
    }

    /// <summary>
    /// Processes a previously uploaded file with cancellation
    /// </summary>
    internal async Task<ProcessingResult> ProcessFileAsync(string executionId, CancellationToken cancellationToken)
    {
        ValidateExecutionId(executionId);
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var requestUri = new Uri($"api/datapipeline/process/{Uri.EscapeDataString(executionId)}", UriKind.Relative);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(
            requestUri,
            null,
            cancellationToken)).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ProcessingResult>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize processing response");
    }

    /// <summary>
    /// Gets the status of a pipeline execution
    /// </summary>
    internal Task<PipelineStatusResponse> GetPipelineStatusAsync(string executionId)
    {
        return GetPipelineStatusAsync(executionId, default);
    }

    /// <summary>
    /// Gets the status of a pipeline execution with cancellation
    /// </summary>
    internal async Task<PipelineStatusResponse> GetPipelineStatusAsync(string executionId, CancellationToken cancellationToken)
    {
        ValidateExecutionId(executionId);
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var requestUri = new Uri($"api/datapipeline/status/{Uri.EscapeDataString(executionId)}", UriKind.Relative);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(requestUri, cancellationToken)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PipelineStatusResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize status response");
    }

    /// <summary>
    /// Gets metrics for a pipeline execution
    /// </summary>
    internal Task<PipelineMetrics> GetPipelineMetricsAsync(string executionId)
    {
        return GetPipelineMetricsAsync(executionId, default);
    }

    /// <summary>
    /// Gets metrics for a pipeline execution with cancellation
    /// </summary>
    internal async Task<PipelineMetrics> GetPipelineMetricsAsync(string executionId, CancellationToken cancellationToken)
    {
        ValidateExecutionId(executionId);
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var relativePath = $"api/datapipeline/metrics/{Uri.EscapeDataString(executionId)}";
        var uri = new Uri(_httpClient.BaseAddress!, relativePath);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(uri, cancellationToken)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PipelineMetrics>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize metrics response");
    }

    /// <summary>
    /// Cancels a running pipeline execution
    /// </summary>
    internal Task<CancelPipelineResponse> CancelPipelineAsync(string executionId)
    {
        return CancelPipelineAsync(executionId, default);
    }

    /// <summary>
    /// Cancels a running pipeline execution with cancellation
    /// </summary>
    internal async Task<CancelPipelineResponse> CancelPipelineAsync(string executionId, CancellationToken cancellationToken)
    {
        ValidateExecutionId(executionId);
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var relativePath = $"api/datapipeline/cancel/{Uri.EscapeDataString(executionId)}";
        var uri = new Uri(_httpClient.BaseAddress!, relativePath);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(uri, null, cancellationToken)).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<CancelPipelineResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize cancel response");
    }

    /// <summary>
    /// Gets all pipeline executions with optional filtering
    /// </summary>
    internal Task<List<PipelineExecution>> GetPipelineExecutionsAsync(CancellationToken cancellationToken)
    {
        return GetPipelineExecutionsAsync(status: null, startTime: null, endTime: null, cancellationToken);
    }

    /// <summary>
    /// Gets all pipeline executions with optional filtering
    /// </summary>
    internal Task<List<PipelineExecution>> GetPipelineExecutionsAsync(PipelineStatus? status, CancellationToken cancellationToken)
    {
        return GetPipelineExecutionsAsync(status, startTime: null, endTime: null, cancellationToken);
    }

    /// <summary>
    /// Gets all pipeline executions with optional filtering
    /// </summary>
    internal async Task<List<PipelineExecution>> GetPipelineExecutionsAsync(
        PipelineStatus? status,
        DateTime? startTime,
        DateTime? endTime,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var query = new System.Collections.Generic.List<string>();

        if (status.HasValue)
        {
            query.Add($"status={Uri.EscapeDataString(status.Value.ToString())}");
        }
        if (startTime.HasValue)
        {
            query.Add($"startTime={Uri.EscapeDataString(startTime.Value.ToString("O"))}");
        }
        if (endTime.HasValue)
        {
            query.Add($"endTime={Uri.EscapeDataString(endTime.Value.ToString("O"))}");
        }

        var queryString = query.Count > 0 ? "?" + string.Join("&", query) : string.Empty;

        var requestUri = new Uri(_httpClient.BaseAddress!, $"api/datapipeline/executions{queryString}");
        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(requestUri, cancellationToken)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<PipelineExecution>>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? new List<PipelineExecution>();
    }

    private async Task<HttpResponseMessage> ExecuteWithResilienceAsync(Func<Task<HttpResponseMessage>> action)
    {
        return await _retryPolicy.WrapAsync(_circuitBreaker).ExecuteAsync(action).ConfigureAwait(false);
    }

    #endregion

    #region MCP Tools

    /// <summary>
    /// Gets all MCP tool configurations
    /// </summary>
    internal Task<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto[]> GetMcpToolsAsync()
    {
        return GetMcpToolsAsync(default);
    }

    /// <summary>
    /// Gets all MCP tool configurations with cancellation
    /// </summary>
    internal async Task<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto[]> GetMcpToolsAsync(CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var uri = new Uri(_httpClient.BaseAddress!, "api/admin/mcp-tools");
        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(uri, cancellationToken)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto[]>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? Array.Empty<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto>();
    }

    /// <summary>
    /// Updates an MCP tool configuration
    /// </summary>
    internal Task<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto> UpdateMcpToolAsync(
        string toolId,
        MotorcycleRAG.Admin.Services.Dtos.UpdateMcpToolRequest request)
    {
        return UpdateMcpToolAsync(toolId, request, default);
    }

    /// <summary>
    /// Updates an MCP tool configuration with cancellation
    /// </summary>
    internal async Task<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto> UpdateMcpToolAsync(
        string toolId,
        MotorcycleRAG.Admin.Services.Dtos.UpdateMcpToolRequest request,
        CancellationToken cancellationToken)
    {
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
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize MCP tool response");
    }

    #endregion

    #region Web Sources

    /// <summary>
    /// Gets all web sources
    /// </summary>
    internal Task<List<WebSource>> GetWebSourcesAsync()
    {
        return GetWebSourcesAsync(default);
    }

    /// <summary>
    /// Gets all web sources with cancellation
    /// </summary>
    internal async Task<List<WebSource>> GetWebSourcesAsync(CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var uri = new Uri(_httpClient.BaseAddress!, "api/admin/web-sources");
        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(uri, cancellationToken)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<WebSource>>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? new List<WebSource>();
    }

    /// <summary>
    /// Adds a new web source
    /// </summary>
    internal Task<WebSource> AddWebSourceAsync(WebSource source)
    {
        return AddWebSourceAsync(source, default);
    }

    /// <summary>
    /// Adds a new web source with cancellation
    /// </summary>
    internal async Task<WebSource> AddWebSourceAsync(WebSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var response = await ExecuteWithResilienceAsync(() =>
            _httpClient.PostAsJsonAsync("api/admin/web-sources", source, _jsonOptions, cancellationToken)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<WebSource>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize web source response");
    }

    /// <summary>
    /// Updates an existing web source
    /// </summary>
    internal Task<WebSource> UpdateWebSourceAsync(WebSource source)
    {
        return UpdateWebSourceAsync(source, default);
    }

    /// <summary>
    /// Updates an existing web source with cancellation
    /// </summary>
    internal async Task<WebSource> UpdateWebSourceAsync(WebSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Id <= 0)
            throw new ArgumentException("Invalid web source ID", nameof(source));

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var response = await ExecuteWithResilienceAsync(() =>
            _httpClient.PutAsJsonAsync($"api/admin/web-sources/{source.Id}", source, _jsonOptions, cancellationToken)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<WebSource>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize web source response");
    }

    /// <summary>
    /// Deletes a web source
    /// </summary>
    internal Task DeleteWebSourceAsync(int sourceId)
    {
        return DeleteWebSourceAsync(sourceId, default);
    }

    /// <summary>
    /// Deletes a web source with cancellation
    /// </summary>
    internal async Task DeleteWebSourceAsync(int sourceId, CancellationToken cancellationToken)
    {
        if (sourceId <= 0)
            throw new ArgumentException("Invalid web source ID", nameof(sourceId));

        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var relativePath = $"api/admin/web-sources/{sourceId}";
        var uri = new Uri(_httpClient.BaseAddress!, relativePath);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.DeleteAsync(uri, cancellationToken)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    #endregion

    #region User Administration

    /// <summary>
    /// Gets all users with optional filtering
    /// </summary>
    internal Task<List<UserDto>> GetUsersAsync(CancellationToken cancellationToken)
    {
        return GetUsersAsync(isEnabled: null, cancellationToken);
    }

    /// <summary>
    /// Gets all users with optional filtering
    /// </summary>
    internal async Task<List<UserDto>> GetUsersAsync(bool? isEnabled, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var query = isEnabled.HasValue ? $"?isEnabled={isEnabled.Value}" : string.Empty;
        var relativePath = $"api/users-admin{query}";
        var uri = new Uri(_httpClient.BaseAddress!, relativePath);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(uri, cancellationToken)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<UserDto>>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? new List<UserDto>();
    }

    /// <summary>
    /// Enables a user account
    /// </summary>
    internal Task<UserDto> EnableUserAsync(string userId)
    {
        return EnableUserAsync(userId, default);
    }

    /// <summary>
    /// Enables a user account with cancellation
    /// </summary>
    internal async Task<UserDto> EnableUserAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var relativePath = $"api/users-admin/{userId}/enable";
        var uri = new Uri(_httpClient.BaseAddress!, relativePath);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(uri, null, cancellationToken)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UserDto>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize user response");
    }

    /// <summary>
    /// Disables a user account
    /// </summary>
    internal Task<UserDto> DisableUserAsync(string userId)
    {
        return DisableUserAsync(userId, default);
    }

    /// <summary>
    /// Disables a user account with cancellation
    /// </summary>
    internal async Task<UserDto> DisableUserAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync().ConfigureAwait(false);

        var relativePath = $"api/users-admin/{userId}/disable";
        var uri = new Uri(_httpClient.BaseAddress!, relativePath);
        var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(uri, null, cancellationToken)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UserDto>(_jsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to deserialize user response");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Validates that executionId is in a valid GUID format
    /// </summary>
    private static void ValidateExecutionId(string executionId) {
        if (string.IsNullOrWhiteSpace(executionId))
            throw new ArgumentException("Execution ID cannot be null or empty", nameof(executionId));

        if (!Guid.TryParse(executionId, out _))
            throw new ArgumentException($"Invalid execution ID format. Expected valid GUID, got: {executionId}", nameof(executionId));
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
