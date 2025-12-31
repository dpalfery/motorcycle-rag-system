using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MotorcycleRAG.Domain.DTOs;

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

    public ApiClient(HttpClient httpClient, IAdminAuthService authService)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
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

        using var content = new MultipartFormDataContent();
        var fileStream = File.OpenRead(filePath);
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
        
        content.Add(streamContent, "file", Path.GetFileName(filePath));

        var response = await _httpClient.PostAsync(
            $"api/datapipeline/upload?processImmediately={processImmediately}",
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<FileUploadResult>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize upload response");
    }

    /// <summary>
    /// Uploads multiple files to the pipeline
    /// </summary>
    public async Task<BatchFileUploadResult> UploadBatchAsync(IEnumerable<string> filePaths, bool processImmediately = false, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        using var content = new MultipartFormDataContent();
        
        foreach (var filePath in filePaths)
        {
            var fileStream = File.OpenRead(filePath);
            var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
            content.Add(streamContent, "files", Path.GetFileName(filePath));
        }

        var response = await _httpClient.PostAsync(
            $"api/datapipeline/upload-batch?processImmediately={processImmediately}",
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<BatchFileUploadResult>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize batch upload response");
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
        await EnsureAuthenticatedAsync();

        var response = await _httpClient.PostAsync(
            $"api/datapipeline/process/{executionId}",
            null,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<ProcessingResult>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize processing response");
    }

    /// <summary>
    /// Gets the status of a pipeline execution
    /// </summary>
    public async Task<PipelineStatusResponse> GetPipelineStatusAsync(string executionId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        var response = await _httpClient.GetAsync($"api/datapipeline/status/{executionId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<PipelineStatusResponse>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize status response");
    }

    /// <summary>
    /// Gets metrics for a pipeline execution
    /// </summary>
    public async Task<PipelineMetrics> GetPipelineMetricsAsync(string executionId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        var response = await _httpClient.GetAsync($"api/datapipeline/metrics/{executionId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<PipelineMetrics>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize metrics response");
    }

    /// <summary>
    /// Cancels a running pipeline execution
    /// </summary>
    public async Task<CancelPipelineResponse> CancelPipelineAsync(string executionId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        var response = await _httpClient.PostAsync(
            $"api/datapipeline/cancel/{executionId}",
            null,
            cancellationToken);

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

        var queryParams = new List<string>();
        if (status.HasValue)
            queryParams.Add($"status={status.Value}");
        if (startTime.HasValue)
            queryParams.Add($"startTime={startTime.Value:O}");
        if (endTime.HasValue)
            queryParams.Add($"endTime={endTime.Value:O}");

        var query = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : string.Empty;
        
        var response = await _httpClient.GetAsync($"api/datapipeline/executions{query}", cancellationToken);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<List<PipelineExecution>>(_jsonOptions, cancellationToken)
               ?? new List<PipelineExecution>();
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
        var response = await _httpClient.GetAsync($"api/users-admin{query}", cancellationToken);
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
