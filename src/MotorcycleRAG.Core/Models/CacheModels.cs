using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Configuration for query result caching
/// </summary>
public class CacheConfiguration
{
    /// <summary>
    /// Default cache duration for search results
    /// </summary>
    public TimeSpan DefaultCacheDuration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum number of cached entries
    /// </summary>
    [Range(100, 10000)]
    public int MaxCacheEntries { get; set; } = 1000;

    /// <summary>
    /// Memory limit for cache in megabytes
    /// </summary>
    [Range(10, 1000)]
    public int MemoryLimitMB { get; set; } = 100;

    /// <summary>
    /// Enable cache compression
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// Cache different durations based on query complexity
    /// </summary>
    public Dictionary<string, TimeSpan> CacheDurationsByType { get; set; } = new()
    {
        { "simple", TimeSpan.FromHours(1) },
        { "complex", TimeSpan.FromMinutes(15) },
        { "web", TimeSpan.FromMinutes(5) },
        { "pdf", TimeSpan.FromHours(2) }
    };
}

/// <summary>
/// Cache performance statistics
/// </summary>
public class CacheStatistics
{
    /// <summary>
    /// Total number of cache requests
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// Number of cache hits
    /// </summary>
    public long CacheHits { get; set; }

    /// <summary>
    /// Number of cache misses
    /// </summary>
    public long CacheMisses { get; set; }

    /// <summary>
    /// Cache hit ratio (0.0 to 1.0)
    /// </summary>
    public double HitRatio => TotalRequests > 0 ? (double)CacheHits / TotalRequests : 0.0;

    /// <summary>
    /// Current number of cached entries
    /// </summary>
    public int CurrentEntries { get; set; }

    /// <summary>
    /// Current memory usage in bytes
    /// </summary>
    public long MemoryUsageBytes { get; set; }

    /// <summary>
    /// Average response time for cached results in milliseconds
    /// </summary>
    public double AverageResponseTimeMs { get; set; }

    /// <summary>
    /// Last reset timestamp
    /// </summary>
    public DateTime LastResetAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Cached query result entry
/// </summary>
public class CachedQueryResult
{
    /// <summary>
    /// Cache key
    /// </summary>
    [Required]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Cached search results
    /// </summary>
    [Required]
    public SearchResult[] Results { get; set; } = Array.Empty<SearchResult>();

    /// <summary>
    /// When the entry was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the entry expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Number of times this entry has been accessed
    /// </summary>
    public int AccessCount { get; set; }

    /// <summary>
    /// Last access timestamp
    /// </summary>
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Size of the cached data in bytes
    /// </summary>
    public long SizeBytes { get; set; }
}