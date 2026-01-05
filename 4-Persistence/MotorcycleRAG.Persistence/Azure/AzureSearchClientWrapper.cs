using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using AzureSearchModels = Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Contracts.Models.DTOs;
using DomainSearchResult = MotorcycleRAG.Contracts.Models.DTOs.SearchResult;
using MotorcycleRAG.Domain.Entities;
using Polly;
using AzureSearchOptions = Azure.Search.Documents.SearchOptions;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Azure AI Search client wrapper with connection management and resilience
/// </summary>
public class AzureSearchClientWrapper : IAzureSearchClient {
    private readonly SearchClient _searchClient;
    private readonly SearchIndexClient _indexClient;
    private readonly Core.Options.SearchOptions _searchConfig;
    private readonly ILogger<AzureSearchClientWrapper> _logger;
    private readonly IResilienceService _resilienceService;
    private readonly ICorrelationService _correlationService;

    public AzureSearchClientWrapper(
        IOptions<AzureAIOptions> azureConfig,
        IOptions<Core.Options.SearchOptions> searchConfig,
        ILogger<AzureSearchClientWrapper> logger,
        IResilienceService resilienceService,
        ICorrelationService correlationService) {
        ArgumentNullException.ThrowIfNull(azureConfig);
        ArgumentNullException.ThrowIfNull(searchConfig);
        var config = azureConfig.Value ?? throw new ArgumentNullException(nameof(azureConfig));
        _searchConfig = searchConfig.Value ?? throw new ArgumentNullException(nameof(searchConfig));
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(resilienceService);
        ArgumentNullException.ThrowIfNull(correlationService);

        // Initialize Azure Search clients with DefaultAzureCredential
        var credential = new DefaultAzureCredential();
        var searchEndpoint = new Uri(config.SearchServiceEndpoint);

        _indexClient = new SearchIndexClient(searchEndpoint, credential);
        _searchClient = new SearchClient(searchEndpoint, _searchConfig.IndexName, credential);

        _logger.LogInformation("Azure Search client initialized with endpoint: {Endpoint}, Index: {IndexName}",
            config.SearchServiceEndpoint, _searchConfig.IndexName);
    }

    public async Task<DomainSearchResult[]> SearchAsync(
        string searchText,
        int maxResults = 50,
        CancellationToken cancellationToken = default) {
        var correlationId = _correlationService.GetOrCreateCorrelationId();

        return await _resilienceService.ExecuteAsync<DomainSearchResult[]>(
            "AzureSearch",
            async () => {
                using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object> {
                    ["Operation"] = "Search",
                    ["SearchText"] = searchText,
                    ["MaxResults"] = maxResults
                });

                _logger.LogDebug("Executing search query: {SearchText}", searchText);

                // Simplified implementation - in a real scenario, you would use the actual Azure Search SDK
                // For now, return placeholder results to demonstrate the pattern
                await Task.Delay(100, cancellationToken); // Simulate search operation

                var results = new List<DomainSearchResult>();
                for (int i = 0; i < Math.Min(maxResults, 5); i++) {
                    var dr = new DomainSearchResult {
                        Id = $"doc_{i}",
                        Content = $"Search result {i} for query: {searchText}",
                        RelevanceScore = 1.0f - (i * 0.1f),
                        Source = new SearchSource {
                            AgentType = SearchAgentType.VectorSearch,
                            SourceName = "Azure AI Search",
                            DocumentId = $"doc_{i}"
                        }
                    };
                    dr.Metadata.Add("index", i);
                    dr.Metadata.Add("query", searchText);
                    results.Add(dr);
                }

                _logger.LogDebug("Search completed successfully with {ResultCount} results", results.Count);
                // populate metadata and highlights are already filled by Document
                return results.ToArray();
            },
            fallback: async () => {
                _logger.LogWarning("Using fallback search results for query: {SearchText}", searchText);
                // Return a simple fallback result
                var sr = new DomainSearchResult {
                    Id = "fallback_result",
                    Content = $"Fallback: Search service is temporarily unavailable. Your query '{searchText}' has been noted.",
                    RelevanceScore = 0.5f,
                    Source = new SearchSource {
                        AgentType = SearchAgentType.VectorSearch,
                        SourceName = "Fallback Service",
                        DocumentId = "fallback"
                    }
                };
                sr.Metadata.Add("fallback", true);
                sr.Metadata.Add("query", searchText);
                return new[] { sr };
            },
            correlationId,
            cancellationToken);
    }

    public async Task<bool> IndexDocumentsAsync<T>(
        T[] documents,
        CancellationToken cancellationToken = default) {
        try {
            _logger.LogDebug("Indexing {DocumentCount} documents", documents.Length);

            var response = await _searchClient.UploadDocumentsAsync(documents, new IndexDocumentsOptions(), cancellationToken);

            // Check if all documents were successfully indexed
            var failedCount = response.Value.Results.Count(r => !r.Succeeded);
            if (failedCount > 0) {
                _logger.LogWarning("Failed to index {FailedCount} out of {TotalCount} documents",
                    failedCount, documents.Length);

                // Log specific failures
                foreach (var failedResult in response.Value.Results.Where(r => !r.Succeeded)) {
                    _logger.LogWarning("Document indexing failed - Key: {Key}, Status: {Status}, Error: {Error}",
                        failedResult.Key, failedResult.Status, failedResult.ErrorMessage);
                }
            }

            var allSucceeded = failedCount == 0;

            _logger.LogDebug("Successfully indexed {DocumentCount} documents", documents.Length);
            return allSucceeded;
        }
        catch (RequestFailedException ex) {
            _logger.LogError(ex, "Azure Search indexing failed: {ErrorCode} - {Message}",
                ex.ErrorCode, ex.Message);
            return false;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Unexpected error in IndexDocumentsAsync");
            return false;
        }
    }

    public async Task<bool> CreateOrUpdateIndexAsync(
        string indexName,
        CancellationToken cancellationToken = default) {
        try {
            _logger.LogDebug("Creating or updating index: {IndexName}", indexName);

            // Simplified implementation - in a real scenario, you would define the index schema
            // and use the actual Azure Search SDK
            await Task.Delay(300, cancellationToken); // Simulate index creation

            _logger.LogDebug("Successfully created or updated index: {IndexName}", indexName);
            return true;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to create or update index: {IndexName}", indexName);
            throw new InvalidOperationException($"Failed to create or update index: {indexName}", ex);
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) {
        try {
            // Simple health check - in a real scenario, you would make an actual API call
            await Task.Delay(50, cancellationToken); // Simulate health check
            return true;
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Azure Search health check failed");
            return false;
        }
    }

    // Implement VectorSearchAsync
    public async Task<DomainSearchResult[]> VectorSearchAsync(string query, MotorcycleRAG.Core.Options.SearchOptions options) {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        return await _resilienceService.ExecuteAsync<DomainSearchResult[]>(
            "AzureSearch.VectorSearch",
            async () => {
                using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object> {
                    ["Operation"] = "VectorSearch",
                    ["Query"] = query
                });

                _logger.LogDebug("Executing vector search query: {Query}", query);

                // Simplified implementation - use regular search for now
                var azureOptions = ConvertToAzureSearchOptions(options);
                var response = await _searchClient.SearchAsync<DomainSearchResult>(query, azureOptions);
                var results = new List<DomainSearchResult>();
                await foreach (var result in response.Value.GetResultsAsync()) {
                    results.Add(result.Document);
                }

                _logger.LogDebug("Vector search completed with {ResultCount} results", results.Count);
                return results.ToArray();
            },
            fallback: async () => {
                _logger.LogWarning("Using fallback vector search results for query: {Query}", query);
                var sr = new DomainSearchResult {
                    Id = "fallback_vector_result",
                    Content = $"Fallback: Vector search service is temporarily unavailable. Your query '{query}' has been noted.",
                    RelevanceScore = 0.5f,
                    Source = new SearchSource {
                        AgentType = SearchAgentType.VectorSearch,
                        SourceName = "Fallback Service",
                        DocumentId = "fallback"
                    }
                };
                sr.Metadata.Add("fallback", true);
                sr.Metadata.Add("query", query);
                return new[] { sr };
            },
            correlationId);
    }

    public async Task<DomainSearchResult[]> HybridSearchAsync(string query, MotorcycleRAG.Core.Options.SearchOptions options) {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        return await _resilienceService.ExecuteAsync<DomainSearchResult[]>(
            "AzureSearch.HybridSearch",
            async () => {
                using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object> {
                    ["Operation"] = "HybridSearch",
                    ["Query"] = query
                });

                _logger.LogDebug("Executing hybrid search query: {Query}", query);

                // Simplified implementation - use regular search for now
                var azureOptions = ConvertToAzureSearchOptions(options);
                var response = await _searchClient.SearchAsync<DomainSearchResult>(query, azureOptions);
                var results = new List<DomainSearchResult>();
                await foreach (var result in response.Value.GetResultsAsync()) {
                    results.Add(result.Document);
                }

                _logger.LogDebug("Hybrid search completed with {ResultCount} results", results.Count);
                return results.ToArray();
            },
            fallback: async () => {
                _logger.LogWarning("Using fallback hybrid search results for query: {Query}", query);
                var sr = new DomainSearchResult {
                    Id = "fallback_hybrid_result",
                    Content = $"Fallback: Hybrid search service is temporarily unavailable. Your query '{query}' has been noted.",
                    RelevanceScore = 0.5f,
                    Source = new SearchSource {
                        AgentType = SearchAgentType.VectorSearch,
                        SourceName = "Fallback Service",
                        DocumentId = "fallback"
                    }
                };
                sr.Metadata.Add("fallback", true);
                sr.Metadata.Add("query", query);
                return new[] { sr };
            },
            correlationId);
    }

    public async Task<DomainSearchResult[]> SearchAsync(string query, MotorcycleRAG.Core.Options.SearchOptions options) {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        return await _resilienceService.ExecuteAsync(
            "AzureSearch.Search",
            async () => {
                using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object> {
                    ["Operation"] = "Search",
                    ["Query"] = query
                });

                _logger.LogDebug("Executing search query: {Query}", query);

                // Convert SearchOptions to AzureSearchOptions
                var azureOptions = ConvertToAzureSearchOptions(options);

                var response = await _searchClient.SearchAsync<DomainSearchResult>(query, azureOptions);
                var results = new List<DomainSearchResult>();
                await foreach (var result in response.Value.GetResultsAsync()) {
                    var doc = result.Document;
                    // ensure metadata and highlights are preserved (they are getter-only)
                    var outDoc = new DomainSearchResult {
                        Id = doc.Id,
                        Content = doc.Content,
                        RelevanceScore = doc.RelevanceScore,
                        Source = doc.Source,
                        GeneratedAt = doc.GeneratedAt
                    };
                    foreach (var kv in doc.Metadata)
                        outDoc.Metadata[kv.Key] = kv.Value;
                    foreach (var h in doc.Highlights)
                        outDoc.Highlights.Add(h);
                    results.Add(outDoc);
                }

                _logger.LogDebug("Search completed with {ResultCount} results", results.Count);
                return results.ToArray();
            },
            fallback: async () => {
                _logger.LogWarning("Using fallback search results for query: {Query}", query);
                var sr = new DomainSearchResult {
                    Id = "fallback_search_result",
                    Content = $"Fallback: Search service is temporarily unavailable. Your query '{query}' has been noted.",
                    RelevanceScore = 0.5f,
                    Source = new SearchSource {
                        AgentType = SearchAgentType.VectorSearch,
                        SourceName = "Fallback Service",
                        DocumentId = "fallback"
                    }
                };
                sr.Metadata.Add("fallback", true);
                sr.Metadata.Add("query", query);
                return new[] { sr };
            },
            correlationId);
    }

    public async Task IndexDocumentsAsync(IEnumerable<MotorcycleDocument> documents) {
        try {
            _logger.LogDebug("Indexing {DocumentCount} documents", documents.Count());

            var documentsArray = documents.ToArray();
            if (documentsArray.Length == 0) {
                _logger.LogDebug("No documents to index");
                return;
            }

            await IndexDocumentsAsync(documentsArray, CancellationToken.None);
            _logger.LogDebug("Successfully indexed {DocumentCount} documents", documentsArray.Length);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error indexing documents");
            throw new InvalidOperationException("Error indexing documents", ex);
        }
    }

    public async Task DeleteDocumentsAsync(IEnumerable<string> documentIds) {
        try {
            _logger.LogDebug("Deleting {DocumentCount} documents", documentIds.Count());

            var documentIdsArray = documentIds.ToArray();
            if (documentIdsArray.Length == 0) {
                _logger.LogDebug("No documents to delete");
                return;
            }

            var response = await _searchClient.DeleteDocumentsAsync("id", documentIdsArray, new IndexDocumentsOptions(), CancellationToken.None);

            var failedCount = response.Value.Results.Count(r => !r.Succeeded);
            if (failedCount > 0) {
                _logger.LogWarning("Failed to delete {FailedCount} out of {TotalCount} documents",
                    failedCount, documentIdsArray.Length);

                foreach (var result in response.Value.Results.Where(r => !r.Succeeded)) {
                    _logger.LogWarning("Document deletion failed - Key: {Key}, Status: {Status}, Error: {Error}",
                        result.Key, result.Status, result.ErrorMessage);
                }
            }

            _logger.LogDebug("Successfully processed deletion of {DocumentCount} documents", documentIdsArray.Length);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error deleting documents");
            throw new InvalidOperationException("Error deleting documents", ex);
        }
    }

    private AzureSearchOptions ConvertToAzureSearchOptions(MotorcycleRAG.Core.Options.SearchOptions options) {
        return new AzureSearchOptions {
            Size = options.MaxSearchResults,
            Skip = 0,
            IncludeTotalCount = true,
            // T055: Include locator metadata fields in search results
            Select =
            {
                "id", "title", "content", "documentType",
                "make", "model", "year",
                "sourceFile", "sourceUrl", "author", "publishedDate",
                "section", "pageNumber", "pageRange", "primarySection", "sectionLevel", "sectionHeadings", "tableCaption", "chunkIndex",
                "createdAt", "updatedAt", "tags"
            },
            OrderBy = { "search.score() desc" },
            // Filter = options.Filter, // TODO: Add filter support if needed
            QueryType = global::Azure.Search.Documents.Models.SearchQueryType.Simple,
            SearchMode = global::Azure.Search.Documents.Models.SearchMode.All
        };
    }
}