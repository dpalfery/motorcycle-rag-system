using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using AzureSearchOptions = Azure.Search.Documents.SearchOptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;


namespace MotorcycleRAG.Persistence.Azure.Search;

/// <summary>
/// Handles Azure AI Search query operations with resilience and correlation tracking
/// </summary>
public class AzureSearchQueryService : IAzureSearchQueryService
{
    private readonly SearchClient _searchClient;
    private readonly ILogger<AzureSearchQueryService> _logger;
    private readonly IResilienceService _resilienceService;
    private readonly ICorrelationService _correlationService;

    public AzureSearchQueryService(
        SearchClient searchClient,
        IOptions<MotorcycleRAG.Core.Options.SearchOptions> searchOptions,
        ILogger<AzureSearchQueryService> logger,
        IResilienceService resilienceService,
        ICorrelationService correlationService)
    {
        _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
        ArgumentNullException.ThrowIfNull(searchOptions);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _correlationService = correlationService ?? throw new ArgumentNullException(nameof(correlationService));
    }

    public async Task<SearchResult[]> VectorSearchAsync(string query, MotorcycleRAG.Core.Options.SearchOptions options)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        return await _resilienceService.ExecuteAsync<SearchResult[]>(
            "AzureSearch.VectorSearch",
            async () => await ExecuteSearchAsync(query, options, "VectorSearch"),
            async () => CreateFallbackResult(query, "VectorSearch"),
            correlationId);
    }

    public async Task<SearchResult[]> HybridSearchAsync(string query, MotorcycleRAG.Core.Options.SearchOptions options)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        return await _resilienceService.ExecuteAsync<SearchResult[]>(
            "AzureSearch.HybridSearch",
            async () => await ExecuteSearchAsync(query, options, "HybridSearch"),
            async () => CreateFallbackResult(query, "HybridSearch"),
            correlationId);
    }

    public async Task<SearchResult[]> SearchAsync(string query, MotorcycleRAG.Core.Options.SearchOptions options)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        return await _resilienceService.ExecuteAsync<SearchResult[]>(
            "AzureSearch.Search",
            async () => await ExecuteSearchAsync(query, options, "Search"),
            async () => CreateFallbackResult(query, "Search"),
            correlationId);
    }

    private async Task<SearchResult[]> ExecuteSearchAsync(string query, MotorcycleRAG.Core.Options.SearchOptions options, string operation)
    {
        using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object>
        {
            ["Operation"] = operation,
            ["QueryLength"] = query.Length
        });

        _logger.LogDebug("Executing {Operation} query with length {QueryLength}",
            LogSanitizer.Sanitize(operation), query.Length);

        var azureOptions = ConvertToAzureSearchOptions(options);
        var response = await _searchClient.SearchAsync<SearchResult>(query, azureOptions);
        
        var results = new List<SearchResult>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            var doc = result.Document;
            var outDoc = new SearchResult
            {
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

        _logger.LogDebug("{Operation} completed with {ResultCount} results",
            LogSanitizer.Sanitize(operation), results.Count);
        return results.ToArray();
    }

    private SearchResult[] CreateFallbackResult(string query, string operation)
    {
        _logger.LogWarning("Using fallback {Operation} results for query length {QueryLength}",
            LogSanitizer.Sanitize(operation), query.Length);
        var result = new SearchResult
        {
            Id = $"fallback_{operation.ToLower()}_result",
            Content = $"Fallback: {operation} service is temporarily unavailable. Your query '{query}' has been noted.",
            RelevanceScore = 0.5f,
            Source = new SearchSource
            {
                AgentType = SearchAgentType.VectorSearch,
                SourceName = "Fallback Service",
                DocumentId = "fallback"
            }
        };
        result.Metadata.Add("fallback", true);
        result.Metadata.Add("query", query);
        return new[] { result };
    }

    private AzureSearchOptions ConvertToAzureSearchOptions(MotorcycleRAG.Core.Options.SearchOptions options)
    {
        return new AzureSearchOptions
        {
            Size = options.MaxSearchResults,
            IncludeTotalCount = true
        };
    }
}
