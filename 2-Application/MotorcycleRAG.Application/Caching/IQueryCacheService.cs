using MotorcycleRAG.Domain.DTOs;


namespace MotorcycleRAG.Application.Caching;

/// <summary>
/// Interface for caching motorcycle query results to improve performance and reduce costs.
/// </summary>
public interface IQueryCacheService
{
    /// <summary>
    /// Gets a cached query response if available.
    /// </summary>
    /// <param name="queryKey">The cache key for the query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached response or null if not found</returns>
    Task<MotorcycleQueryResponse?> GetAsync(string queryKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a query response in the cache.
    /// </summary>
    /// <param name="queryKey">The cache key for the query</param>
    /// <param name="response">The response to cache</param>
    /// <param name="expiration">Cache expiration time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SetAsync(string queryKey, MotorcycleQueryResponse response, TimeSpan expiration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a cached query response.
    /// </summary>
    /// <param name="queryKey">The cache key to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RemoveAsync(string queryKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a cache key for a query request.
    /// </summary>
    /// <param name="request">The query request</param>
    /// <returns>A unique cache key</returns>
    string GenerateCacheKey(MotorcycleQueryRequest request);

    /// <summary>
    /// Clears all cached entries.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cache statistics</returns>
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Cache statistics for monitoring and optimization.
/// </summary>
public class CacheStatistics
{
    public long TotalRequests { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public double HitRatio => TotalRequests > 0 ? (double)CacheHits / TotalRequests : 0;
    public long TotalEntries { get; set; }
    public long TotalMemoryUsage { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
