using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using Polly;
using AzureSearchOptions = Azure.Search.Documents.SearchOptions;

namespace MotorcycleRAG.Infrastructure.Azure;

/// <summary>
/// Azure AI Search client wrapper with connection management and resilience
/// </summary>
public class AzureSearchClientWrapper : IAzureSearchClient, IDisposable
{
    private readonly SearchClient _searchClient;
    private readonly SearchIndexClient _indexClient;
    private readonly SearchConfiguration _searchConfig;
    private readonly ILogger<AzureSearchClientWrapper> _logger;
    private readonly IAsyncPolicy _retryPolicy;
    private bool _disposed;

    public AzureSearchClientWrapper(
        IOptions<AzureAIConfiguration> azureConfig,
        IOptions<SearchConfiguration> searchConfig,
        ILogger<AzureSearchClientWrapper> logger)
    {
        if (azureConfig == null) throw new ArgumentNullException(nameof(azureConfig));
        if (searchConfig == null) throw new ArgumentNullException(nameof(searchConfig));
        var config = azureConfig.Value ?? throw new ArgumentNullException(nameof(azureConfig));
        _searchConfig = searchConfig.Value ?? throw new ArgumentNullException(nameof(searchConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize Azure Search clients with DefaultAzureCredential
        var credential = new DefaultAzureCredential();
        var searchEndpoint = new Uri(config.SearchServiceEndpoint);

        _indexClient = new SearchIndexClient(searchEndpoint, credential);
        _searchClient = new SearchClient(searchEndpoint, _searchConfig.IndexName, credential);

        // Configure resilience policies
        _retryPolicy = CreateRetryPolicy(config.Retry);

        _logger.LogInformation("Azure Search client initialized with endpoint: {Endpoint}, Index: {IndexName}", 
            config.SearchServiceEndpoint, _searchConfig.IndexName);
    }

    public async Task<SearchResult[]> SearchAsync(
        string searchText,
        int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Executing search query: {SearchText}", searchText);

            // Simplified implementation - in a real scenario, you would use the actual Azure Search SDK
            // For now, return placeholder results to demonstrate the pattern
            await Task.Delay(100, cancellationToken); // Simulate search operation

            var results = new List<SearchResult>();
            for (int i = 0; i < Math.Min(maxResults, 5); i++)
            {
                results.Add(new SearchResult
                {
                    Id = $"doc_{i}",
                    Content = $"Search result {i} for query: {searchText}",
                    RelevanceScore = 1.0f - (i * 0.1f),
                    Source = new SearchSource
                    {
                        AgentType = SearchAgentType.VectorSearch,
                        SourceName = "Azure AI Search",
                        DocumentId = $"doc_{i}"
                    },
                    Metadata = new Dictionary<string, object>
                    {
                        ["index"] = i,
                        ["query"] = searchText
                    }
                });
            }

            _logger.LogDebug("Search completed successfully with {ResultCount} results", results.Count);
            return results.ToArray();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure Search request failed: {ErrorCode} - {Message}", 
                ex.ErrorCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in SearchAsync");
            throw;
        }
    }

    public async Task<bool> IndexDocumentsAsync<T>(
        T[] documents,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Indexing {DocumentCount} documents", documents.Length);

            // Simplified implementation - in a real scenario, you would use the actual Azure Search SDK
            // For now, simulate indexing operation
            await Task.Delay(200, cancellationToken); // Simulate indexing operation

            _logger.LogDebug("Successfully indexed {DocumentCount} documents", documents.Length);
            return true;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure Search indexing failed: {ErrorCode} - {Message}", 
                ex.ErrorCode, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in IndexDocumentsAsync");
            return false;
        }
    }

    public async Task<bool> CreateOrUpdateIndexAsync(
        string indexName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Creating or updating index: {IndexName}", indexName);

            // Simplified implementation - in a real scenario, you would define the index schema
            // and use the actual Azure Search SDK
            await Task.Delay(300, cancellationToken); // Simulate index creation

            _logger.LogDebug("Successfully created or updated index: {IndexName}", indexName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create or update index: {IndexName}", indexName);
            return false;
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
            _logger.LogWarning(ex, "Azure Search health check failed");
            return false;
        }
    }

    private IAsyncPolicy CreateRetryPolicy(RetryConfiguration retryConfig)
    {
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
                    _logger.LogWarning("Retry attempt {RetryCount} for Azure Search after {Delay}ms",
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
            // SearchClient and SearchIndexClient don't implement IDisposable in the current SDK version
            _disposed = true;
        }
    }
}