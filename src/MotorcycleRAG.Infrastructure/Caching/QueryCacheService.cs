using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MotorcycleRAG.Infrastructure.Caching;

/// <summary>
/// Implementation of query result caching service
/// </summary>
public class QueryCacheService : IQueryCacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<QueryCacheService> _logger;
    private readonly CacheConfiguration _configuration;
    private readonly CacheStatistics _statistics;
    private readonly object _statsLock = new();

    public QueryCacheService(
        IMemoryCache memoryCache,
        ILogger<QueryCacheService> logger,
        IOptions<CacheConfiguration> configuration)
    {
        _memoryCache = memoryCache;
        _logger = logger;
        _configuration = configuration.Value;
        _statistics = new CacheStatistics();
    }

    public async Task<SearchResult[]?> GetCachedResultsAsync(string cacheKey)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            
            lock (_statsLock)
            {
                _statistics.TotalRequests++;
            }

            if (_memoryCache.TryGetValue(cacheKey, out CachedQueryResult? cachedResult))
            {
                lock (_statsLock)
                {
                    _statistics.CacheHits++;
                    var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    _statistics.AverageResponseTimeMs = 
                        (_statistics.AverageResponseTimeMs * (_statistics.CacheHits - 1) + responseTime) / _statistics.CacheHits;
                }

                cachedResult!.AccessCount++;
                cachedResult.LastAccessedAt = DateTime.UtcNow;

                _logger.LogDebug("Cache hit for key: {CacheKey}", cacheKey);
                return await Task.FromResult(cachedResult.Results);
            }

            lock (_statsLock)
            {
                _statistics.CacheMisses++;
            }

            _logger.LogDebug("Cache miss for key: {CacheKey}", cacheKey);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cached results for key: {CacheKey}", cacheKey);
            return null;
        }
    }

    public async Task SetCachedResultsAsync(string cacheKey, SearchResult[] results, TimeSpan cacheDuration)
    {
        try
        {
            var cachedResult = new CachedQueryResult
            {
                Key = cacheKey,
                Results = results,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(cacheDuration),
                AccessCount = 0,
                SizeBytes = EstimateSize(results)
            };

            var cacheEntryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = cacheDuration,
                Priority = CacheItemPriority.Normal,
                Size = cachedResult.SizeBytes
            };

            // Add eviction callback to update statistics
            cacheEntryOptions.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
            {
                EvictionCallback = OnCacheEntryEvicted,
                State = cachedResult
            });

            _memoryCache.Set(cacheKey, cachedResult, cacheEntryOptions);

            lock (_statsLock)
            {
                _statistics.CurrentEntries++;
                _statistics.MemoryUsageBytes += cachedResult.SizeBytes;
            }

            _logger.LogDebug("Cached results for key: {CacheKey}, Duration: {Duration}, Size: {Size} bytes", 
                cacheKey, cacheDuration, cachedResult.SizeBytes);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching results for key: {CacheKey}", cacheKey);
        }
    }

    public string GenerateCacheKey(string query, SearchOptions options, SearchAgentType agentType)
    {
        try
        {
            var keyData = new
            {
                Query = query.Trim().ToLowerInvariant(),
                AgentType = agentType.ToString(),
                Options = new
                {
                    options.MaxResults,
                    options.MinRelevanceScore,
                    options.Filters,
                    options.IncludeMetadata,
                    options.EnableCaching
                }
            };

            var json = JsonSerializer.Serialize(keyData);
            var hash = ComputeHash(json);
            
            return $"query_cache:{agentType}:{hash}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating cache key for query: {Query}", query);
            return $"query_cache:{agentType}:{Guid.NewGuid()}";
        }
    }

    public async Task InvalidateCacheAsync(string pattern)
    {
        try
        {
            // Note: MemoryCache doesn't support pattern-based invalidation natively
            // This is a simplified implementation that would need enhancement for production
            _logger.LogInformation("Cache invalidation requested for pattern: {Pattern}", pattern);
            
            if (pattern == "*")
            {
                // Clear all cache entries
                if (_memoryCache is MemoryCache mc)
                {
                    mc.Compact(1.0); // Remove all entries
                    
                    lock (_statsLock)
                    {
                        _statistics.CurrentEntries = 0;
                        _statistics.MemoryUsageBytes = 0;
                    }
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cache with pattern: {Pattern}", pattern);
        }
    }

    public CacheStatistics GetCacheStatistics()
    {
        lock (_statsLock)
        {
            return new CacheStatistics
            {
                TotalRequests = _statistics.TotalRequests,
                CacheHits = _statistics.CacheHits,
                CacheMisses = _statistics.CacheMisses,
                CurrentEntries = _statistics.CurrentEntries,
                MemoryUsageBytes = _statistics.MemoryUsageBytes,
                AverageResponseTimeMs = _statistics.AverageResponseTimeMs,
                LastResetAt = _statistics.LastResetAt
            };
        }
    }

    private void OnCacheEntryEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        if (state is CachedQueryResult cachedResult)
        {
            lock (_statsLock)
            {
                _statistics.CurrentEntries = Math.Max(0, _statistics.CurrentEntries - 1);
                _statistics.MemoryUsageBytes = Math.Max(0, _statistics.MemoryUsageBytes - cachedResult.SizeBytes);
            }

            _logger.LogDebug("Cache entry evicted: {Key}, Reason: {Reason}", key, reason);
        }
    }

    private static long EstimateSize(SearchResult[] results)
    {
        try
        {
            var json = JsonSerializer.Serialize(results);
            return Encoding.UTF8.GetByteCount(json);
        }
        catch
        {
            // Fallback estimation
            return results.Length * 1024; // Assume 1KB per result
        }
    }

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16]; // Use first 16 characters
    }
}