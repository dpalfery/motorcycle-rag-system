using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Agents;

/// <summary>
/// Web search agent - orchestrates web search via injected services.
/// </summary>
public sealed class WebSearchAgent : ISearchAgent, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly WebSearchOptions _config;
    private readonly ILogger<WebSearchAgent> _logger;
    private readonly WebSearchAgentServices _services;
    private bool _disposed;

    public SearchAgentType AgentType => SearchAgentType.WebSearch;

    public WebSearchAgent(
        HttpClient httpClient,
        IOptions<WebSearchOptions> config,
        ILogger<WebSearchAgent> logger,
        WebSearchAgentServices services)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(services);

        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
        _services = services;

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
            using var rateLimit = await _services.RateLimiter.AcquireAsync();

            // Check cache first (only when enabled)
            if (options.EnableCaching && _services.Cache.TryGetCachedResults(query, options, out var cachedResults))
            {
                _logger.LogInformation("Cache hit for query: {Query}", query);
                return cachedResults;
            }

            // Enhance search terms (generate multiple terms and search across them)
            var searchTerms = await _services.TermEnhancer.GenerateSearchTermsAsync(query, CancellationToken.None);

            // Execute searches across sources and terms
            var allResults = new List<SearchResult>();
            foreach (var source in _config.TrustedSources)
            {
                foreach (var term in searchTerms)
                {
                    var sourceResults = await SearchSourceAsync(source, term, options);
                    allResults.AddRange(sourceResults);
                }
            }

            // Validate sources + enrich metadata (trust policy tier, AI quality, etc.)
            var validatedResults = await _services.Validator.ValidateResultsAsync(allResults, CancellationToken.None);

            // Format and rank
            var formattedResults = FormatResults(validatedResults.ToList(), query, options.IncludeMetadata);
            var finalResults = RankAndFilter(formattedResults, options);

            // Cache results (only when enabled)
            if (options.EnableCaching)
            {
                _services.Cache.CacheResults(query, options, finalResults);
            }

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "Web search completed in {Duration}ms with {Count} results",
                duration.TotalMilliseconds,
                finalResults.Length);

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
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MotorcycleRAG/1.0 (Educational Research Bot)");
        }

        _httpClient.Timeout = TimeSpan.FromSeconds(_config.RequestTimeoutSeconds);
    }

    private async Task<List<SearchResult>> SearchSourceAsync(
        TrustedSourceOptions source,
        string searchTerm,
        SearchParameters options)
    {
        var results = new List<SearchResult>();

        try
        {
            var searchUrl = BuildSearchUrl(source, searchTerm);
            var html = await FetchWebContentAsync(searchUrl);

            if (string.IsNullOrWhiteSpace(html))
            {
                return results;
            }

            var extracted = _services.Extractor.ExtractFromHtml(
                html,
                source.ContentSelector ?? "//p",
                maxResults: Math.Max(1, options.MaxResults));

            if (extracted.Count > 0)
            {
                results.AddRange(ConvertToSearchResults(extracted, searchTerm, source, searchUrl, options.IncludeMetadata));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search {Source} for: {Query}", source.Name, searchTerm);
        }

        var perSourceLimit = _config.TrustedSources.Count > 0
            ? Math.Max(1, options.MaxResults / _config.TrustedSources.Count)
            : options.MaxResults;

        return results.Take(perSourceLimit).ToList();
    }

    private static Uri BuildSearchUrl(TrustedSourceOptions source, string searchTerm)
    {
        var encodedTerm = Uri.EscapeDataString(searchTerm);
        var url = source.SearchUrlTemplate?.ToString()?.Replace("{query}", encodedTerm) ?? string.Empty;
        return new Uri(url, UriKind.Absolute);
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

    private static List<SearchResult> ConvertToSearchResults(
        IReadOnlyCollection<MotorcycleRAG.Application.Services.Web.ExtractedContent> contents,
        string searchTerm,
        TrustedSourceOptions source,
        Uri searchUrl,
        bool includeMetadata)
    {
        return contents.Select(content =>
        {
            var result = new SearchResult
            {
                Id = $"web_{Guid.NewGuid()}",
                Content = content.Text,
                RelevanceScore = CalculateRelevanceScore(content.Text, searchTerm),
                Source = new SearchSource
                {
                    AgentType = SearchAgentType.WebSearch,
                    SourceName = source.Name,
                    SourceUrl = searchUrl.ToString(),
                    LastUpdated = DateTime.UtcNow
                },
                GeneratedAt = DateTime.UtcNow
            };

            if (includeMetadata)
            {
                result.Metadata["searchTerm"] = searchTerm;
                result.Metadata["sourceType"] = "web";
                result.Metadata["credibilityScore"] = source.CredibilityScore;
            }

            return result;
        }).ToList();
    }

    private static float CalculateRelevanceScore(string content, string searchTerm)
    {
        var contentUpper = content.ToUpperInvariant();
        var score = 0.3f;

        if (contentUpper.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.3f;
        }

        var searchWords = searchTerm.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordMatches = searchWords.Count(word => contentUpper.Contains(word, StringComparison.Ordinal));
        score += (wordMatches / (float)Math.Max(1, searchWords.Length)) * 0.4f;

        return Math.Min(1.0f, score);
    }

    private static List<SearchResult> FormatResults(List<SearchResult> results, string query, bool includeMetadata)
    {
        return results.Select(r =>
        {
            r.Content = $"[Web Source: {r.Source.SourceName}] {r.Content}";

            if (includeMetadata)
            {
                r.Metadata["integrationType"] = "webAugmentation";
                r.Metadata["originalQuery"] = query;

                // Backfill sourceType if earlier pipeline didn't set it
                if (!r.Metadata.ContainsKey("sourceType"))
                {
                    r.Metadata["sourceType"] = "web";
                }
            }

            return r;
        }).ToList();
    }

    private static SearchResult[] RankAndFilter(List<SearchResult> results, SearchParameters options)
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

    private void Dispose(bool disposing)
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

