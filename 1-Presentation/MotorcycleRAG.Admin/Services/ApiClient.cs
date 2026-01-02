using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MotorcycleRAG.Admin.Services.Dtos;
using MotorcycleRAG.Domain.DTOs;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// HTTP client wrapper for calling the Motorcycle RAG API.
/// Handles authentication, request/response serialization, and error handling.
/// </summary>
public class ApiClient
{
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
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .OrResult<HttpResponseMessage>(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                                                (int)r.StatusCode == 503)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (outcome, timespan, retryCount, context) =>
                {
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
    /// Ensures the HTTP client has a valid access token
    /// </summary>
    private async Task EnsureAuthenticatedAsync()
    {
        var accessToken = await _authService.GetAccessTokenAsync();
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
    public async Task<FileUploadResult> UploadFileAsync(string filePath, bool processImmediately = false, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));

            content.Add(streamContent, "file", Path.GetFileName(filePath));

            var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(
                $"api/datapipeline/upload?processImmediately={processImmediately}",
                content,
                cancellationToken));

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<FileUploadResult>(_jsonOptions, cancellationToken);
            UploadResultValidator.ValidateFileUploadResult(result);
            return result!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file: {FilePath}", Path.GetFileName(filePath));
            throw;
        }
    }

    /// <summary>
    /// Uploads multiple files to the pipeline
    /// </summary>
    public async Task<BatchFileUploadResult> UploadBatchAsync(IEnumerable<string> filePaths, bool processImmediately = false, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        var streams = new List<FileStream>();  // Track all opened streams

        try
        {
            using var content = new MultipartFormDataContent();

            foreach (var filePath in filePaths)
            {
                // Validate before opening
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {filePath}");

                var fileStream = File.OpenRead(filePath);
                streams.Add(fileStream);  // Track for cleanup

                var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
                content.Add(streamContent, "files", Path.GetFileName(filePath));
            }

            var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(
                $"api/datapipeline/upload-batch?processImmediately={processImmediately}",
                content,
                cancellationToken));

            response.EnsureSuccessStatusCode();

            var batchResult = await response.Content.ReadFromJsonAsync<BatchFileUploadResult>(_jsonOptions, cancellationToken);
            UploadResultValidator.ValidateBatchFileUploadResult(batchResult);
            return batchResult!;
        }  // <-- content disposed here AFTER PostAsync completes
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading batch files");
            throw;
        }
        finally
        {
            // Ensure all streams are disposed even on exception
            foreach (var stream in streams)
            {
                stream?.Dispose();
            }
        }
    }

    /// <summary>
    /// Gets upload constraints (file size limits, allowed types, etc.)
    /// </summary>
    public async Task<UploadConstraints> GetUploadConstraintsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        var response = await _httpClient.GetAsync("api/datapipeline/upload-constraints", cancellationToken);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<UploadConstraints>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize constraints response");
    }

    #endregion

    #region Pipeline Processing

    /// <summary>
    /// Processes a previously uploaded file
    /// </summary>
    public async Task<ProcessingResult> ProcessFileAsync(string executionId, CancellationToken cancellationToken = default)
    {
        ValidateExecutionId(executionId);
        await EnsureAuthenticatedAsync();

        var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(
            $"api/datapipeline/process/{Uri.EscapeDataString(executionId)}",
            null,
            cancellationToken));

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ProcessingResult>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize processing response");
    }

    /// <summary>
    /// Gets the status of a pipeline execution
    /// </summary>
    public async Task<PipelineStatusResponse> GetPipelineStatusAsync(string executionId, CancellationToken cancellationToken = default)
    {
        ValidateExecutionId(executionId);
        await EnsureAuthenticatedAsync();

        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync($"api/datapipeline/status/{Uri.EscapeDataString(executionId)}", cancellationToken));
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PipelineStatusResponse>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize status response");
    }

    /// <summary>
    /// Gets metrics for a pipeline execution
    /// </summary>
    public async Task<PipelineMetrics> GetPipelineMetricsAsync(string executionId, CancellationToken cancellationToken = default)
    {
        ValidateExecutionId(executionId);
        await EnsureAuthenticatedAsync();

        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync($"api/datapipeline/metrics/{Uri.EscapeDataString(executionId)}", cancellationToken));
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PipelineMetrics>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize metrics response");
    }

    /// <summary>
    /// Cancels a running pipeline execution
    /// </summary>
    public async Task<CancelPipelineResponse> CancelPipelineAsync(string executionId, CancellationToken cancellationToken = default)
    {
        ValidateExecutionId(executionId);
        await EnsureAuthenticatedAsync();

        var response = await ExecuteWithResilienceAsync(() => _httpClient.PostAsync(
            $"api/datapipeline/cancel/{Uri.EscapeDataString(executionId)}",
            null,
            cancellationToken));

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<CancelPipelineResponse>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize cancel response");
    }

    /// <summary>
    /// Gets all pipeline executions with optional filtering
    /// </summary>
    public async Task<List<PipelineExecution>> GetPipelineExecutionsAsync(
        PipelineStatus? status = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        var query = new System.Collections.Generic.List<string>();

        if (status.HasValue)
            query.Add($"status={Uri.EscapeDataString(status.Value.ToString())}");
        if (startTime.HasValue)
            query.Add($"startTime={Uri.EscapeDataString(startTime.Value.ToString("O"))}");
        if (endTime.HasValue)
            query.Add($"endTime={Uri.EscapeDataString(endTime.Value.ToString("O"))}");

        var queryString = query.Count > 0 ? "?" + string.Join("&", query) : string.Empty;

        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync($"api/datapipeline/executions{queryString}", cancellationToken));
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<PipelineExecution>>(_jsonOptions, cancellationToken)
               ?? new List<PipelineExecution>();
    }

    private async Task<HttpResponseMessage> ExecuteWithResilienceAsync(Func<Task<HttpResponseMessage>> action)
    {
        return await _retryPolicy.WrapAsync(_circuitBreaker).ExecuteAsync(action);
    }

    #endregion

    #region MCP Tools

    /// <summary>
    /// Gets all MCP tool configurations
    /// </summary>
    public async Task<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto[]> GetMcpToolsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync("api/admin/mcp-tools", cancellationToken));
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto[]>(_jsonOptions, cancellationToken)
               ?? Array.Empty<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto>();
    }

    /// <summary>
    /// Updates an MCP tool configuration
    /// </summary>
    public async Task<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto> UpdateMcpToolAsync(string toolId, MotorcycleRAG.Admin.Services.Dtos.UpdateMcpToolRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toolId))
            throw new ArgumentException("Tool ID cannot be null or empty", nameof(toolId));
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        await EnsureAuthenticatedAsync();

        var response = await ExecuteWithResilienceAsync(() =>
            _httpClient.PutAsJsonAsync($"api/admin/mcp-tools/{Uri.EscapeDataString(toolId)}", request, _jsonOptions, cancellationToken));
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MotorcycleRAG.Admin.Services.Dtos.McpToolConfigurationDto>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize MCP tool response");
    }

    #endregion

    #region Web Sources

    /// <summary>
    /// Gets all web sources
    /// </summary>
    public async Task<List<WebSource>> GetWebSourcesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync("api/admin/web-sources", cancellationToken));
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<WebSource>>(_jsonOptions, cancellationToken)
               ?? new List<WebSource>();
    }

    /// <summary>
    /// Adds a new web source
    /// </summary>
    public async Task<WebSource> AddWebSourceAsync(WebSource source, CancellationToken cancellationToken = default)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        await EnsureAuthenticatedAsync();

        var response = await ExecuteWithResilienceAsync(() => 
            _httpClient.PostAsJsonAsync("api/admin/web-sources", source, _jsonOptions, cancellationToken));
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<WebSource>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize web source response");
    }

    /// <summary>
    /// Updates an existing web source
    /// </summary>
    public async Task<WebSource> UpdateWebSourceAsync(WebSource source, CancellationToken cancellationToken = default)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        if (source.Id <= 0)
            throw new ArgumentException("Invalid web source ID", nameof(source));

        await EnsureAuthenticatedAsync();

        var response = await ExecuteWithResilienceAsync(() =>
            _httpClient.PutAsJsonAsync($"api/admin/web-sources/{source.Id}", source, _jsonOptions, cancellationToken));
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<WebSource>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize web source response");
    }

    /// <summary>
    /// Deletes a web source
    /// </summary>
    public async Task DeleteWebSourceAsync(int sourceId, CancellationToken cancellationToken = default)
    {
        if (sourceId <= 0)
            throw new ArgumentException("Invalid web source ID", nameof(sourceId));

        await EnsureAuthenticatedAsync();

        var response = await ExecuteWithResilienceAsync(() =>
            _httpClient.DeleteAsync($"api/admin/web-sources/{sourceId}", cancellationToken));
        response.EnsureSuccessStatusCode();
    }

    #endregion

    #region User Administration

    /// <summary>
    /// Gets all users with optional filtering
    /// </summary>
    public async Task<List<UserDto>> GetUsersAsync(bool? isEnabled = null, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        var query = isEnabled.HasValue ? $"?isEnabled={isEnabled.Value}" : string.Empty;
        var uri = $"api/users-admin{query}";
        var response = await ExecuteWithResilienceAsync(() => _httpClient.GetAsync(uri, cancellationToken));
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<List<UserDto>>(_jsonOptions, cancellationToken)
               ?? new List<UserDto>();
    }

    /// <summary>
    /// Enables a user account
    /// </summary>
    public async Task<UserDto> EnableUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        var response = await _httpClient.PostAsync($"api/users-admin/{userId}/enable", null, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<UserDto>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize user response");
    }

    /// <summary>
    /// Disables a user account
    /// </summary>
    public async Task<UserDto> DisableUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        var response = await _httpClient.PostAsync($"api/users-admin/{userId}/disable", null, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<UserDto>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize user response");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Validates that executionId is in a valid GUID format
    /// </summary>
    private static void ValidateExecutionId(string executionId)
    {
        if (string.IsNullOrWhiteSpace(executionId))
            throw new ArgumentException("Execution ID cannot be null or empty", nameof(executionId));

        if (!Guid.TryParse(executionId, out _))
            throw new ArgumentException($"Invalid execution ID format. Expected valid GUID, got: {executionId}", nameof(executionId));
    }

    /// <summary>
    /// Gets the MIME content type for a file based on its extension
    /// </summary>
    private static string GetContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    #endregion
}

/// <summary>
/// Response for cancel pipeline operation
/// </summary>
public class CancelPipelineResponse
{
    public string ExecutionId { get; set; } = string.Empty;
    public bool Cancelled { get; set; }
}

/// <summary>
/// Response for get pipeline status operation
/// </summary>
public class PipelineStatusResponse
{
    public string ExecutionId { get; set; } = string.Empty;
    public PipelineStatus Status { get; set; }
}

/// <summary>
/// DTO for user information
/// </summary>
public class UserDto
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string PlanSku { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSignInAt { get; set; }
}

/// <summary>
/// Pipeline execution information
/// </summary>
public class PipelineExecution
{
    public string ExecutionId { get; set; } = string.Empty;
    public string PipelineType { get; set; } = string.Empty;
    public PipelineStatus Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Upload constraints information
/// </summary>
public class UploadConstraints
{
    public long MaxFileSizeBytes { get; set; }
    public int MaxFilesPerBatch { get; set; }
    public List<string> AllowedFileTypes { get; set; } = new();
}
