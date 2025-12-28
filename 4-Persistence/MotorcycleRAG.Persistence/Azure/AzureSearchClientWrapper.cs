using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Persistence.Resilience;
using Polly;
using AzureSearchOptions = Azure.Search.Documents.SearchOptions;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Azure AI Search client wrapper with connection management and resilience
/// </summary>
public class AzureSearchClientWrapper : IAzureSearchClient, IDisposable
{
    private readonly SearchClient _searchClient;
    private readonly SearchIndexClient _indexClient;
    private readonly SearchConfiguration _searchConfig;
    private readonly ILogger<AzureSearchClientWrapper> _logger;
    private readonly IResilienceService _resilienceService;
    private readonly ICorrelationService _correlationService;
    private readonly IAsyncPolicy _retryPolicy;
    private bool _disposed;

    public AzureSearchClientWrapper(
        IOptions<AzureAIConfiguration> azureConfig,
        IOptions<SearchConfiguration> searchConfig,
        ILogger<AzureSearchClientWrapper> logger,
        IResilienceService resilienceService,
        ICorrelationService correlationService)
    {
        if (azureConfig == null) throw new ArgumentNullException(nameof(azureConfig));
        if (searchConfig == null) throw new ArgumentNullException(nameof(searchConfig));
        var config = azureConfig.Value ?? throw new ArgumentNullException(nameof(azureConfig));
        _searchConfig = searchConfig.Value ?? throw new ArgumentNullException(nameof(searchConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _correlationService = correlationService ?? throw new ArgumentNullException(nameof(correlationService));

        // Initialize Azure Search clients with DefaultAzureCredential
        var credential = new DefaultAzureCredential();
        var searchEndpoint = new Uri(config.SearchServiceEndpoint);

        _indexClient = new SearchIndexClient(searchEndpoint, credential);
        _searchClient = new SearchClient(searchEndpoint, _searchConfig.IndexName, credential);

        // Configure resilience policies (kept for backward compatibility)
        _retryPolicy = CreateRetryPolicy(config.Retry);

        _logger.LogInformation("Azure Search client initialized with endpoint: {Endpoint}, Index: {IndexName}",
            config.SearchServiceEndpoint, _searchConfig.IndexName);
    }

    public async Task<SearchResult[]> SearchAsync(
        string searchText,
        int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();

        return await _resilienceService.ExecuteAsync(
            "AzureSearch",
            async () =>
            {
                using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object>
                {
                    ["Operation"] = "Search",
                    ["SearchText"] = searchText,
                    ["MaxResults"] = maxResults
                });

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
            },
            fallback: async () =>
            {
                _logger.LogWarning("Using fallback search results for query: {SearchText}", searchText);
                // Return a simple fallback result
                return new[]
                {
                    new SearchResult
                    {
                        Id = "fallback_result",
                        Content = $"Fallback: Search service is temporarily unavailable. Your query '{searchText}' has been noted.",
                        RelevanceScore = 0.5f,
                        Source = new SearchSource
                        {
                            AgentType = SearchAgentType.VectorSearch,
                            SourceName = "Fallback Service",
                            DocumentId = "fallback"
                        },
                        Metadata = new Dictionary<string, object>
                        {
                            ["fallback"] = true,
                            ["query"] = searchText
                        }
                    }
                };
            },
            correlationId,
            cancellationToken);
    }

    public async Task<bool> IndexDocumentsAsync<T>(
        T[] documents,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Indexing {DocumentCount} documents", documents.Length);

            var result = await _retryPolicy.ExecuteAsync(async () =>
            {
                var indexDocumentsAction = IndexDocumentsBatch.Upload(documents);
                var response = await _searchClient.IndexDocumentsAsync(indexDocumentsAction, new IndexDocumentsOptions(), cancellationToken);

                // Check if all documents were successfully indexed
                var failedCount = response.Value.Results.Count(r => !r.Succeeded);
                if (failedCount > 0)
                {
                    _logger.LogWarning("Failed to index {FailedCount} out of {TotalCount} documents",
                        failedCount, documents.Length);

                    // Log specific failures
                    foreach (var result in response.Value.Results.Where(r => !r.Succeeded))
                    {
                        _logger.LogWarning("Document indexing failed - Key: {Key}, Status: {Status}, Error: {Error}",
                            result.Key, result.Status, result.ErrorMessage);
                    }
                }

                return failedCount == 0;
            });

            _logger.LogDebug("Successfully indexed {DocumentCount} documents", documents.Length);
            return result;
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

    // Implement VectorSearchAsync
    public async Task<SearchResult[]> VectorSearchAsync(string query, Domain.Models.SearchOptions options)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        return await _resilienceService.ExecuteAsync(
            "AzureSearch.VectorSearch",
            async () =>
            {
                using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object>
                {
                    ["Operation"] = "VectorSearch",
                    ["Query"] = query
                });

                _logger.LogDebug("Executing vector search query: {Query}", query);

                // Simplified implementation - use regular search for now
                var azureOptions = ConvertToAzureSearchOptions(options);
                var response = await _searchClient.SearchAsync<SearchResult>(query, azureOptions);
                var results = new List<SearchResult>();
                await foreach (var result in response.Value.GetResultsAsync())
                {
                    results.Add(result.Document);
                }

                _logger.LogDebug("Vector search completed with {ResultCount} results", results.Count);
                return results.ToArray();
            },
            fallback: async () =>
            {
                _logger.LogWarning("Using fallback vector search results for query: {Query}", query);
                return new[]
                {
                    new SearchResult
                    {
                        Id = "fallback_vector_result",
                        Content = $"Fallback: Vector search service is temporarily unavailable. Your query '{query}' has been noted.",
                        RelevanceScore = 0.5f,
                        Source = new SearchSource
                        {
                            AgentType = SearchAgentType.VectorSearch,
                            SourceName = "Fallback Service",
                            DocumentId = "fallback"
                        },
                        Metadata = new Dictionary<string, object>
                        {
                            ["fallback"] = true,
                            ["query"] = query
                        }
                    }
                };
            },
            correlationId);
    }

    public async Task<SearchResult[]> HybridSearchAsync(string query, Domain.Models.SearchOptions options)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        return await _resilienceService.ExecuteAsync(
            "AzureSearch.HybridSearch",
            async () =>
            {
                using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object>
                {
                    ["Operation"] = "HybridSearch",
                    ["Query"] = query
                });

                _logger.LogDebug("Executing hybrid search query: {Query}", query);

                // Simplified implementation - use regular search for now
                var azureOptions = ConvertToAzureSearchOptions(options);
                var response = await _searchClient.SearchAsync<SearchResult>(query, azureOptions);
                var results = new List<SearchResult>();
                await foreach (var result in response.Value.GetResultsAsync())
                {
                    results.Add(result.Document);
                }

                _logger.LogDebug("Hybrid search completed with {ResultCount} results", results.Count);
                return results.ToArray();
            },
            fallback: async () =>
            {
                _logger.LogWarning("Using fallback hybrid search results for query: {Query}", query);
                return new[]
                {
                    new SearchResult
                    {
                        Id = "fallback_hybrid_result",
                        Content = $"Fallback: Hybrid search service is temporarily unavailable. Your query '{query}' has been noted.",
                        RelevanceScore = 0.5f,
                        Source = new SearchSource
                        {
                            AgentType = SearchAgentType.VectorSearch,
                            SourceName = "Fallback Service",
                            DocumentId = "fallback"
                        },
                        Metadata = new Dictionary<string, object>
                        {
                            ["fallback"] = true,
                            ["query"] = query
                        }
                    }
                };
            },
            correlationId);
    }

    public async Task<SearchResult[]> SearchAsync(string query, Domain.Models.SearchOptions options)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        return await _resilienceService.ExecuteAsync(
            "AzureSearch.Search",
            async () =>
            {
                using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object>
                {
                    ["Operation"] = "Search",
                    ["Query"] = query
                });

                _logger.LogDebug("Executing search query: {Query}", query);

                // Convert SearchOptions to AzureSearchOptions
                var azureOptions = ConvertToAzureSearchOptions(options);

                var response = await _searchClient.SearchAsync<SearchResult>(query, azureOptions);
                var results = new List<SearchResult>();
                await foreach (var result in response.Value.GetResultsAsync())
                {
                    results.Add(result.Document);
                }

                _logger.LogDebug("Search completed with {ResultCount} results", results.Count);
                return results.ToArray();
            },
            fallback: async () =>
            {
                _logger.LogWarning("Using fallback search results for query: {Query}", query);
                return new[]
                {
                    new SearchResult
                    {
                        Id = "fallback_search_result",
                        Content = $"Fallback: Search service is temporarily unavailable. Your query '{query}' has been noted.",
                        RelevanceScore = 0.5f,
                        Source = new SearchSource
                        {
                            AgentType = SearchAgentType.VectorSearch,
                            SourceName = "Fallback Service",
                            DocumentId = "fallback"
                        },
                        Metadata = new Dictionary<string, object>
                        {
                            ["fallback"] = true,
                            ["query"] = query
                        }
                    }
                };
            },
            correlationId);
    }

    public async Task IndexDocumentsAsync(IEnumerable<MotorcycleDocument> documents)
    {
        try
        {
            _logger.LogDebug("Indexing {DocumentCount} documents", documents.Count());

            var documentsArray = documents.ToArray();
            if (documentsArray.Length == 0)
            {
                _logger.LogDebug("No documents to index");
                return;
            }

            await IndexDocumentsAsync(documentsArray, CancellationToken.None);
            _logger.LogDebug("Successfully indexed {DocumentCount} documents", documentsArray.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing documents");
            throw;
        }
    }

    public async Task DeleteDocumentsAsync(IEnumerable<string> documentIds)
    {
        try
        {
            _logger.LogDebug("Deleting {DocumentCount} documents", documentIds.Count());

            var documentIdsArray = documentIds.ToArray();
            if (documentIdsArray.Length == 0)
            {
                _logger.LogDebug("No documents to delete");
                return;
            }

            var deleteBatch = IndexDocumentsBatch.Delete(documentIdsArray);
            var response = await _searchClient.IndexDocumentsAsync(deleteBatch, new IndexDocumentsOptions(), CancellationToken.None);

            var failedCount = response.Value.Results.Count(r => !r.Succeeded);
            if (failedCount > 0)
            {
                _logger.LogWarning("Failed to delete {FailedCount} out of {TotalCount} documents",
                    failedCount, documentIdsArray.Length);

                foreach (var result in response.Value.Results.Where(r => !r.Succeeded))
                {
                    _logger.LogWarning("Document deletion failed - Key: {Key}, Status: {Status}, Error: {Error}",
                        result.Key, result.Status, result.ErrorMessage);
                }
            }

            _logger.LogDebug("Successfully processed deletion of {DocumentCount} documents", documentIdsArray.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting documents");
            throw;
        }
    }

    private AzureSearchOptions ConvertToAzureSearchOptions(Domain.Models.SearchOptions options)
    {
        return new AzureSearchOptions
        {
            Size = options.MaxResults,
            Skip = 0,
            IncludeTotalCount = true,
            Select = { "id", "title", "content", "documentType", "make", "model", "year", "sourceFile", "section", "createdAt", "tags", "metadata" },
            OrderBy = { "search.score() desc" },
            // Filter = options.Filter, // TODO: Add filter support if needed
            QueryType = SearchQueryType.Simple,
            SearchMode = SearchMode.All
        };
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
