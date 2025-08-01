using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using MotorcycleRAG.Infrastructure.Resilience;
using Polly;

namespace MotorcycleRAG.Infrastructure.Azure;

/// <summary>
/// Azure OpenAI client wrapper with retry policies and authentication
/// </summary>
public class AzureOpenAIClientWrapper : IAzureOpenAIClient, IDisposable
{
    private readonly AzureOpenAIClient _client;
    private readonly AzureAIConfiguration _config;
    private readonly ILogger<AzureOpenAIClientWrapper> _logger;
    private readonly IResilienceService _resilienceService;
    private readonly ICorrelationService _correlationService;
    private readonly IAsyncPolicy _retryPolicy;
    private bool _disposed;

    public AzureOpenAIClientWrapper(
        IOptions<AzureAIConfiguration> config,
        ILogger<AzureOpenAIClientWrapper> logger,
        IResilienceService resilienceService,
        ICorrelationService correlationService)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        _config = config.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _correlationService = correlationService ?? throw new ArgumentNullException(nameof(correlationService));

        // Initialize Azure OpenAI client with DefaultAzureCredential
        var credential = new DefaultAzureCredential();
        _client = new AzureOpenAIClient(new Uri(_config.OpenAIEndpoint), credential);

        // Configure retry policy with exponential backoff (kept for backward compatibility)
        _retryPolicy = CreateRetryPolicy();

        _logger.LogInformation("Azure OpenAI client initialized with endpoint: {Endpoint}", 
            _config.OpenAIEndpoint);
    }

    public async Task<string> GetChatCompletionAsync(
        string deploymentName,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        
        return await _resilienceService.ExecuteAsync(
            "AzureOpenAI",
            async () =>
            {
                using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object>
                {
                    ["Operation"] = "GetChatCompletion",
                    ["DeploymentName"] = deploymentName
                });

                _logger.LogDebug("Getting chat completion for deployment: {DeploymentName}", deploymentName);

                // Simplified implementation - in a real scenario, you would use the actual Azure OpenAI SDK
                // For now, return a placeholder to demonstrate the pattern
                await Task.Delay(100, cancellationToken); // Simulate API call
                
                _logger.LogDebug("Successfully retrieved chat completion");
                return $"Chat completion response for: {prompt}";
            },
            fallback: async () =>
            {
                _logger.LogWarning("Using fallback response for chat completion");
                return $"Fallback response: Unable to process request at this time. Please try again later.";
            },
            correlationId,
            cancellationToken);
    }

    public async Task<float[]> GetEmbeddingAsync(
        string deploymentName,
        string text,
        CancellationToken cancellationToken = default)
    {
        var embeddings = await GetEmbeddingsAsync(deploymentName, new[] { text }, cancellationToken);
        return embeddings[0];
    }

    public async Task<float[][]> GetEmbeddingsAsync(
        string deploymentName,
        string[] texts,
        CancellationToken cancellationToken = default)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        
        return await _resilienceService.ExecuteAsync(
            "AzureOpenAI",
            async () =>
            {
                using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object>
                {
                    ["Operation"] = "GetEmbeddings",
                    ["DeploymentName"] = deploymentName,
                    ["TextCount"] = texts.Length
                });

                _logger.LogDebug("Getting embeddings for deployment: {DeploymentName}, Text count: {TextCount}", 
                    deploymentName, texts.Length);

                // Simplified implementation - in a real scenario, you would use the actual Azure OpenAI SDK
                // For now, return placeholder embeddings to demonstrate the pattern
                await Task.Delay(100, cancellationToken); // Simulate API call

                var embeddings = texts.Select(text => 
                    Enumerable.Range(0, 1536).Select(i => (float)Random.Shared.NextDouble()).ToArray()
                ).ToArray();

                _logger.LogDebug("Successfully retrieved embeddings");
                return embeddings;
            },
            fallback: async () =>
            {
                _logger.LogWarning("Using fallback embeddings for {TextCount} texts", texts.Length);
                // Return zero embeddings as fallback
                return texts.Select(text => 
                    new float[1536] // All zeros
                ).ToArray();
            },
            correlationId,
            cancellationToken);
    }

    public async Task<string> ProcessMultimodalContentAsync(
        string deploymentName,
        string textPrompt,
        byte[] imageData,
        string imageContentType = "image/jpeg",
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Processing multimodal content for deployment: {DeploymentName}", deploymentName);

            // Simplified implementation - in a real scenario, you would use the actual Azure OpenAI SDK
            // For now, return a placeholder to demonstrate the pattern
            await Task.Delay(200, cancellationToken); // Simulate API call
            
            _logger.LogDebug("Successfully processed multimodal content");
            return $"GPT-4 Vision analysis of image ({imageData.Length} bytes): {textPrompt}";
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure OpenAI multimodal request failed: {ErrorCode} - {Message}", 
                ex.ErrorCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in ProcessMultimodalContentAsync");
            throw;
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Simple health check - in a real scenario, you would make an actual API call
            await Task.Delay(50, cancellationToken); // Simulate health check
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure OpenAI health check failed");
            return false;
        }
    }

    private IAsyncPolicy CreateRetryPolicy()
    {
        var retryConfig = _config.Retry;
        
        return Policy
            .Handle<RequestFailedException>(ex => IsRetryableError(ex))
            .Or<TaskCanceledException>()
            .Or<HttpRequestException>()
            .WaitAndRetryAsync(
                retryCount: retryConfig.MaxRetries,
                sleepDurationProvider: retryAttempt => retryConfig.UseExponentialBackoff
                    ? TimeSpan.FromSeconds(Math.Min(
                        retryConfig.BaseDelaySeconds * Math.Pow(2, retryAttempt - 1),
                        retryConfig.MaxDelaySeconds))
                    : TimeSpan.FromSeconds(retryConfig.BaseDelaySeconds),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("Retry attempt {RetryCount} for Azure OpenAI after {Delay}ms",
                        retryCount, timespan.TotalMilliseconds);
                });
    }

    private static bool IsRetryableError(RequestFailedException ex)
    {
        // Retry on rate limiting, server errors, and timeout
        return ex.Status == 429 || // Too Many Requests
               ex.Status == 500 || // Internal Server Error
               ex.Status == 502 || // Bad Gateway
               ex.Status == 503 || // Service Unavailable
               ex.Status == 504;   // Gateway Timeout
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // AzureOpenAIClient doesn't implement IDisposable in the current SDK version
            _disposed = true;
        }
    }
}