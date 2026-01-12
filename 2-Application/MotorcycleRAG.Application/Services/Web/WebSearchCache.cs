using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services.Web;

/// <summary>
/// In-memory cache for web search results with time-based expiration
/// </summary>
public class WebSearchCache
{
    private readonly ConcurrentDictionary<string, CachedSearchResults> _cache;
    private readonly TimeSpan _cacheExpiration;
    private readonly int _maxCacheSize;
    private readonly ILogger<WebSearchCache> _logger;

    public WebSearchCache(ILogger<WebSearchCache> logger)
        : this(TimeSpan.FromMinutes(15), 100, logger)
    {
    }

    public WebSearchCache(
        TimeSpan cacheExpiration,
        int maxCacheSize,
        ILogger<WebSearchCache> logger)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCacheSize, 1);
        if (cacheExpiration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheExpiration), cacheExpiration, "cacheExpiration must be greater than zero");
        }
        ArgumentNullException.ThrowIfNull(logger);

        _cache = new ConcurrentDictionary<string, CachedSearchResults>();
        _cacheExpiration = cacheExpiration;
        _maxCacheSize = maxCacheSize;
        _logger = logger;
    }

    public bool TryGet(string query, out IReadOnlyCollection<SearchResult> results)
    {
        ArgumentNullException.ThrowIfNull(query);

        results = new List<SearchResult>();

        var cacheKey = query.ToUpperInvariant();
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.Results.Count > 0)
        {
            var cacheAge = DateTime.UtcNow - cached.CachedAt;
            if (cacheAge < _cacheExpiration)
            {
                results = cached.Results;
                _logger.LogDebug("Cache hit for query: {Query}", query);
                return true;
            }
            else
            {
                _cache.TryRemove(cacheKey, out _);
                _logger.LogDebug("Cache expired for query: {Query}", query);
            }
        }

        return false;
    }

    public bool TryGetCachedResults(string query, SearchParameters options, out SearchResult[] results)
    {
        ArgumentNullException.ThrowIfNull(options);

        results = Array.Empty<SearchResult>();

        if (TryGet(query, out var cachedResults))
        {
            results = cachedResults.Take(options.MaxResults).ToArray();
            return true;
        }
        
        return false;
    }

    public void Set(string query, IReadOnlyCollection<SearchResult> results)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(results);

        var cacheKey = query.ToUpperInvariant();

        // Enforce cache size limit
        if (_cache.Count >= _maxCacheSize)
        {
            var oldestKey = _cache.Keys.ElementAt(0);
            _cache.TryRemove(oldestKey, out _);
            _logger.LogDebug("Cache size limit reached, removed oldest entry");
        }

        _cache[cacheKey] = new CachedSearchResults
        {
            Results = results.ToList(),
            CachedAt = DateTime.UtcNow
        };

        _logger.LogDebug("Cached {Count} results for query: {Query}", results.Count, query);
    }

    public void Clear()
    {
        _cache.Clear();
        _logger.LogInformation("Search cache cleared");
    }

    public void CacheResults(string query, SearchParameters options, SearchResult[] results)
    {
        var resultList = results.ToList();
        Set(query, resultList);
    }

    private class CachedSearchResults
    {
        public List<SearchResult> Results { get; init; } = new();
        public DateTime CachedAt { get; init; }
    }
}