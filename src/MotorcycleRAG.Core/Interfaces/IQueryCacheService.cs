using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Interface for caching search query results
/// </summary>
public interface IQueryCacheService
{
    /// <summary>
    /// Get cached search results for a query
    /// </summary>
    /// <param name="cacheKey">The cache key for the query</param>
    /// <returns>Cached search results or null if not found</returns>
    Task<SearchResult[]?> GetCachedResultsAsync(string cacheKey);

    /// <summary>
    /// Cache search results for a query
    /// </summary>
    /// <param name="cacheKey">The cache key for the query</param>
    /// <param name="results">The search results to cache</param>
    /// <param name="cacheDuration">How long to cache the results</param>
    Task SetCachedResultsAsync(string cacheKey, SearchResult[] results, TimeSpan cacheDuration);

    /// <summary>
    /// Generate a consistent cache key for a query and options
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="options">The search options</param>
    /// <param name="agentType">The type of search agent</param>
    /// <returns>A consistent cache key</returns>
    string GenerateCacheKey(string query, SearchOptions options, SearchAgentType agentType);

    /// <summary>
    /// Invalidate cached results by pattern
    /// </summary>
    /// <param name="pattern">Pattern to match cache keys for invalidation</param>
    Task InvalidateCacheAsync(string pattern);

    /// <summary>
    /// Get cache statistics
    /// </summary>
    /// <returns>Cache performance statistics</returns>
    CacheStatistics GetCacheStatistics();
}