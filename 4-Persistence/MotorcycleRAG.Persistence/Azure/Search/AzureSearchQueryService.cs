using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using AzureSearchOptions = Azure.Search.Documents.SearchOptions;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;
using MotorcycleRAG.Domain.ValueObjects;


namespace MotorcycleRAG.Persistence.Azure.Search;

/// <summary>
/// Handles Azure AI Search query operations across the four category-partitioned
/// indexes (D4), with resilience and correlation tracking.
/// </summary>
/// <remarks>
/// <para>
/// A query whose <see cref="SearchOptions.Category"/> resolves to a valid
/// <see cref="MotorcycleCategory"/> is routed to that single index. When no valid
/// category is supplied the query <b>fans out</b> across all four indexes in parallel
/// and the per-index results are merged by relevance score (descending) and truncated
/// to the requested maximum.
/// </para>
/// <para>
/// The <see cref="SearchClient"/> for each index is resolved on demand from
/// <see cref="ISearchClientFactory"/> (no singleton client bound to one index remains).
/// </para>
/// </remarks>
public class AzureSearchQueryService : IAzureSearchQueryService
{
    private readonly ISearchClientFactory _clientFactory;
    private readonly ILogger<AzureSearchQueryService> _logger;
    private readonly IResilienceService _resilienceService;
    private readonly ICorrelationService _correlationService;

    public AzureSearchQueryService(
        ISearchClientFactory clientFactory,
        ILogger<AzureSearchQueryService> logger,
        IResilienceService resilienceService,
        ICorrelationService correlationService)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _correlationService = correlationService ?? throw new ArgumentNullException(nameof(correlationService));
    }

    public async Task<SearchResult[]> VectorSearchAsync(string query, MotorcycleRAG.Core.Options.SearchOptions options)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        return await _resilienceService.ExecuteAsync<SearchResult[]>(
            "AzureSearch.VectorSearch",
            async () => await ExecuteRoutedSearchAsync(query, options, "VectorSearch"),
            async () => CreateFallbackResult(query, "VectorSearch"),
            correlationId);
    }

    public async Task<SearchResult[]> HybridSearchAsync(string query, MotorcycleRAG.Core.Options.SearchOptions options)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        return await _resilienceService.ExecuteAsync<SearchResult[]>(
            "AzureSearch.HybridSearch",
            async () => await ExecuteRoutedSearchAsync(query, options, "HybridSearch"),
            async () => CreateFallbackResult(query, "HybridSearch"),
            correlationId);
    }

    public async Task<SearchResult[]> SearchAsync(string query, MotorcycleRAG.Core.Options.SearchOptions options)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        return await _resilienceService.ExecuteAsync<SearchResult[]>(
            "AzureSearch.Search",
            async () => await ExecuteRoutedSearchAsync(query, options, "Search"),
            async () => CreateFallbackResult(query, "Search"),
            correlationId);
    }

    /// <summary>
    /// Routes the query to a single category index (when <paramref name="options"/>
    /// carries a valid <see cref="SearchOptions.Category"/>) or fans out across all four
    /// indexes and merges by score.
    /// </summary>
    private async Task<SearchResult[]> ExecuteRoutedSearchAsync(
        string query,
        MotorcycleRAG.Core.Options.SearchOptions options,
        string operation)
    {
        if (MotorcycleCategory.TryParse(options.Category, out var category) && category.IsDefined)
        {
            using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object>
            {
                ["Operation"] = operation,
                ["QueryLength"] = query.Length,
                ["Category"] = category.Value,
                ["IndexName"] = _clientFactory.GetIndexName(category)
            });

            _logger.LogDebug("Executing {Operation} against category index {IndexName}",
                LogSanitizer.Sanitize(operation), _clientFactory.GetIndexName(category));

            var single = await ExecuteSearchAsync(query, options, _clientFactory.GetClient(category), operation)
                .ConfigureAwait(false);
            return Truncate(single, options.MaxSearchResults);
        }

        // Fan-out: query every category index in parallel, drop per-index failures, merge by score.
        using var fanScope = _correlationService.CreateLoggingScope(new Dictionary<string, object>
        {
            ["Operation"] = operation,
            ["QueryLength"] = query.Length,
            ["FanOut"] = _clientFactory.AllCategories.Count
        });

        _logger.LogDebug("Fanning out {Operation} across {IndexCount} category indexes",
            LogSanitizer.Sanitize(operation), _clientFactory.AllCategories.Count);

        var perIndexTasks = _clientFactory.AllCategories
            .Select(c => ExecuteSearchResilientAsync(query, options, _clientFactory.GetClient(c), operation))
            .ToArray();

        var perIndex = await Task.WhenAll(perIndexTasks).ConfigureAwait(false);

        var merged = MergeByScore(perIndex, options.MaxSearchResults);

        _logger.LogDebug("{Operation} fan-out merged to {ResultCount} results",
            LogSanitizer.Sanitize(operation), merged.Count);

        return merged.ToArray();
    }

    /// <summary>
    /// Executes the search against a single resolved <paramref name="searchClient"/>,
    /// wrapped in the resilience pipeline. On failure returns an empty array so that a
    /// single failed index never aborts a fan-out merge.
    /// </summary>
    private async Task<SearchResult[]> ExecuteSearchResilientAsync(
        string query,
        MotorcycleRAG.Core.Options.SearchOptions options,
        SearchClient searchClient,
        string operation)
    {
        try
        {
            return await ExecuteSearchAsync(query, options, searchClient, operation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "{Operation} against one category index failed during fan-out; index contribution dropped",
                LogSanitizer.Sanitize(operation));
            return Array.Empty<SearchResult>();
        }
    }

    private async Task<SearchResult[]> ExecuteSearchAsync(
        string query,
        MotorcycleRAG.Core.Options.SearchOptions options,
        SearchClient searchClient,
        string operation)
    {
        _logger.LogDebug("Executing {Operation} query with length {QueryLength}",
            LogSanitizer.Sanitize(operation), query.Length);

        var azureOptions = ConvertToAzureSearchOptions(options);
        var response = await searchClient.SearchAsync<SearchResult>(query, azureOptions)
            .ConfigureAwait(false);

        var results = new List<SearchResult>();
        await foreach (var result in response.Value.GetResultsAsync().ConfigureAwait(false))
        {
            var doc = result.Document;
            var outDoc = new SearchResult
            {
                Id = doc.Id,
                Content = doc.Content,
                RelevanceScore = (float)result.Score.GetValueOrDefault(),
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

    /// <summary>
    /// Merges per-index result sets by relevance score (descending) and truncates to
    /// <paramref name="topN"/>. Pure &amp; deterministic so fan-out ordering is unit-testable.
    /// </summary>
    internal static List<SearchResult> MergeByScore(IEnumerable<SearchResult[]> perIndexResults, int topN)
    {
        var merged = perIndexResults
            .Where(set => set is not null)
            .SelectMany(set => set)
            .OrderByDescending(r => r.RelevanceScore)
            .ToList();

        if (topN > 0 && merged.Count > topN)
        {
            merged.RemoveRange(topN, merged.Count - topN);
        }

        return merged;
    }

    private static SearchResult[] Truncate(SearchResult[] results, int topN)
    {
        if (topN <= 0 || results.Length <= topN)
        {
            return results;
        }

        var truncated = new SearchResult[topN];
        Array.Copy(results, 0, truncated, 0, topN);
        return truncated;
    }

    private SearchResult[] CreateFallbackResult(string query, string operation)
    {
        _logger.LogWarning("Using fallback {Operation} results for query length {QueryLength}",
            LogSanitizer.Sanitize(operation), query.Length);
        var result = new SearchResult
        {
            Id = $"fallback_{operation.ToLower()}result",
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
