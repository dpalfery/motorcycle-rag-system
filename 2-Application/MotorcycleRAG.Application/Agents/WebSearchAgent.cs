using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Enums;
using System.Threading;
using MotorcycleRAG.Application.Services.Web;
using WebExtractor = MotorcycleRAG.Application.Services.Web.WebContentExtractor;

namespace MotorcycleRAG.Application.Agents;

/// <summary>
/// Web search agent - now focused only on orchestrating specialized components
/// </summary>
public class WebSearchAgent : ISearchAgent, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly WebSearchOptions _config;
    private readonly ILogger<WebSearchAgent> _logger;
    private readonly WebSearchRateLimiter _rateLimiter;
    private readonly WebSearchCache _cache;
    private readonly WebExtractor _extractor;
    private readonly WebSearchTermEnhancer _termEnhancer;
    private readonly WebSourceValidator _validator;
    private bool _disposed;

    public SearchAgentType AgentType => SearchAgentType.WebSearch;

    public WebSearchAgent(
        HttpClient httpClient,
        IOptions<WebSearchOptions> config,
        ILogger<WebSearchAgent> logger,
        WebSearchRateLimiter rateLimiter,
        WebSearchCache cache,
        WebExtractor extractor,
        WebSearchTermEnhancer termEnhancer,
        WebSourceValidator validator)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(rateLimiter);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(termEnhancer);
        ArgumentNullException.ThrowIfNull(validator);

        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
        _rateLimiter = rateLimiter;
        _cache = cache;
        _extractor = extractor;
        _termEnhancer = termEnhancer;
        _validator = validator;

        ConfigureHttpClient();
    }

    /// <summary>
    /// Execute web search with rate limiting and credibility validation
    /// </summary>
    public async Task<SearchResult[]> SearchAsync(string query, SearchParameters options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogWarning("Empty query provided");
            return Array.Empty<SearchResult>();
        }

        try
        {
            _logger.LogInformation("Executing web search for query: {Query}", query);
            var startTime = DateTime.UtcNow;

            // Use rate limiter
            using var rateLimit = await _rateLimiter.AcquireAsync();

            // Check cache first
            if (_cache.TryGetCachedResults(query, options, out var cachedResults))
            {
                _logger.LogInformation("Cache hit for query: {Query}", query);
                return cachedResults;
            }

            // Enhance search terms
            var enhancedQuery = await _termEnhancer.EnhanceSearchTermsAsync(query);

            // Execute searches across sources
            var allResults = new List<SearchResult>();
            foreach (var source in _config.TrustedSources)
            {
                var sourceResults = await SearchSourceAsync(source, enhancedQuery, options);
                allResults.AddRange(sourceResults);
            }

            // Extract content using the extractor
            var extractedResults = new List<SearchResult>();
            foreach (var result in allResults)
            {
                var extractedContent = await _extractor.ExtractContentAsync(result.Content);
                if (!string.IsNullOrWhiteSpace(extractedContent.Text))
                {
                    result.Content = extractedContent.Text;
                    extractedResults.Add(result);
                }
            }

            // Validate sources
            var validatedResults = new List<SearchResult>();
            foreach (var result in extractedResults)
            {
                var isValid = await _validator.ValidateSourceAsync(result.Source.SourceName, result.Content);
                if (isValid)
                {
                    validatedResults.Add(result);
                }
            }

            // Format and rank
            var formattedResults = FormatResults(validatedResults, query);
            var finalResults = RankAndFilter(formattedResults, options);

            // Cache results
            _cache.CacheResults(query, options, finalResults);

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Web search completed in {Duration}ms with {Count} results",
                duration.TotalMilliseconds, finalResults.Length);

            return finalResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web search failed for query: {Query}", query);
            throw new InvalidOperationException($"Web search failed: {ex.Message}", ex);
        }
    }

    private void ConfigureHttpClient()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "MotorcycleRAG/1.0 (Educational Research Bot)");
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.RequestTimeoutSeconds);
    }

    private async Task<List<SearchResult>> SearchSourceAsync(
        TrustedSourceOptions source,
        string query,
        SearchParameters options)
    {
        var results = new List<SearchResult>();

        try
        {
            var searchUrl = BuildSearchUrl(source, query);

            var content = await FetchWebContentAsync(searchUrl);

            if (!string.IsNullOrWhiteSpace(content))
            {
                // Use the extractor to extract content
                var extractedContent = await _extractor.ExtractContentAsync(content);
                if (!string.IsNullOrWhiteSpace(extractedContent.Text))
                {
                    results.AddRange(ConvertToSearchResults(new List<ExtractedContent> { extractedContent }, query, source));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search {Source} for: {Query}", source.Name, query);
        }

        return results.Take(options.MaxResults / _config.TrustedSources.Count).ToList();
    }

    private Uri BuildSearchUrl(TrustedSourceOptions source, string searchTerm)
    {
        var encodedTerm = Uri.EscapeDataString(searchTerm);
        var url = source.SearchUrlTemplate?.ToString()?.Replace("{query}", encodedTerm) ?? string.Empty;
        return new Uri(url);
    }

    private async Task<string> FetchWebContentAsync(Uri uri)
    {
        try
        {
            using var response = await _httpClient.GetAsync(uri);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTTP error fetching {Url}", uri);
            return string.Empty;
        }
    }

    private List<SearchResult> ConvertToSearchResults(
        List<ExtractedContent> contents,
        string searchTerm,
        TrustedSourceOptions source)
    {
        return contents.Select(content => new SearchResult
        {
            Id = $"web_{Guid.NewGuid()}",
            Content = content.Text,
            RelevanceScore = CalculateRelevanceScore(content.Text, searchTerm),
            Source = new SearchSource
            {
                AgentType = SearchAgentType.WebSearch,
                SourceName = source.Name,
                SourceUrl = source.BaseUrl?.ToString(),
                LastUpdated = DateTime.UtcNow
            },
            GeneratedAt = DateTime.UtcNow
        }).ToList();
    }

    private float CalculateRelevanceScore(string content, string searchTerm)
    {
        var contentUpper = content.ToUpperInvariant();
        var score = 0.3f;

        if (contentUpper.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.3f;
        }

        var searchWords = searchTerm.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordMatches = searchWords.Count(word => contentUpper.Contains(word));
        score += (wordMatches / (float)searchWords.Length) * 0.4f;

        return Math.Min(1.0f, score);
    }

    private List<SearchResult> FormatResults(List<SearchResult> results, string query)
    {
        return results.Select(r =>
        {
            r.Content = $"[Web Source: {r.Source.SourceName}] {r.Content}";
            r.Metadata["integrationType"] = "webAugmentation";
            r.Metadata["originalQuery"] = query;
            return r;
        }).ToList();
    }

    private SearchResult[] RankAndFilter(List<SearchResult> results, SearchParameters options)
    {
        return results
            .Where(r => r.RelevanceScore >= options.MinRelevanceScore)
            .OrderByDescending(r => r.RelevanceScore)
            .Take(options.MaxResults)
            .ToArray();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // No owned disposable dependencies to clean up
            // HttpClient is injected and managed by IHttpClientFactory
        }

        _disposed = true;
    }
}
