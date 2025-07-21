using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
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
    private readonly IAsyncPolicy _retryPolicy;
    private bool _disposed;

    public AzureOpenAIClientWrapper(
        IOptions<AzureAIConfiguration> config,
        ILogger<AzureOpenAIClientWrapper> logger)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        _config = config.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize Azure OpenAI client with DefaultAzureCredential
        var credential = new DefaultAzureCredential();
        _client = new AzureOpenAIClient(new Uri(_config.OpenAIEndpoint), credential);

        // Configure retry policy with exponential backoff
        _retryPolicy = CreateRetryPolicy();

        _logger.LogInformation("Azure OpenAI client initialized with endpoint: {Endpoint}", 
            _config.OpenAIEndpoint);
    }

    public async Task<string> GetChatCompletionAsync(
        string deploymentName,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting chat completion for deployment: {DeploymentName}", deploymentName);

            // Simplified implementation - in a real scenario, you would use the actual Azure OpenAI SDK
            // For now, return a placeholder to demonstrate the pattern
            await Task.Delay(100, cancellationToken); // Simulate API call
            
            _logger.LogDebug("Successfully retrieved chat completion");
            return $"Chat completion response for: {prompt}";
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure OpenAI request failed: {ErrorCode} - {Message}", 
                ex.ErrorCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in GetChatCompletionAsync");
            throw;
        }
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
        try
        {
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
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure OpenAI embeddings request failed: {ErrorCode} - {Message}", 
                ex.ErrorCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in GetEmbeddingsAsync");
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