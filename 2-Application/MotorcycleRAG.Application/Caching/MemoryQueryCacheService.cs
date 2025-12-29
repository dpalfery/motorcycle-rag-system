using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Domain.DTOs;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;


namespace MotorcycleRAG.Application.Caching;

/// <summary>
/// In-memory implementation of query caching service with LRU eviction and compression.
/// </summary>
public class MemoryQueryCacheService : IQueryCacheService, IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<MemoryQueryCacheService> _logger;
    private readonly CacheConfiguration _config;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _statsLock = new();
    private CacheStatistics _statistics = new();
    private bool _disposed;

    public MemoryQueryCacheService(
        IMemoryCache memoryCache,
        ILogger<MemoryQueryCacheService> logger,
        IOptions<CacheConfiguration> config)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        _logger.LogInformation("Memory query cache service initialized with max size: {MaxSize}MB",
            _config.MaxMemorySizeMB);
    }

    public async Task<MotorcycleQueryResponse?> GetAsync(string queryKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryKey))
            return null;

        try
        {
            lock (_statsLock)
            {
                _statistics.TotalRequests++;
            }

            if (_memoryCache.TryGetValue(queryKey, out var cachedData))
            {
                lock (_statsLock)
                {
                    _statistics.CacheHits++;
                }

                if (cachedData is byte[] data)
                {
                    var response = DeserializeResponse(data);
                    _logger.LogDebug("Cache hit for query key: {QueryKey}", queryKey);
                    return response;
                }
            }

            lock (_statsLock)
            {
                _statistics.CacheMisses++;
            }

            _logger.LogDebug("Cache miss for query key: {QueryKey}", queryKey);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrieving from cache for key: {QueryKey}", queryKey);
            return null;
        }
    }

    public async Task SetAsync(string queryKey, MotorcycleQueryResponse response, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryKey) || response == null)
            return;

        try
        {
            var serializedData = SerializeResponse(response);

            var cacheEntryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration,
                Size = serializedData.Length,
                Priority = DetermineCachePriority(response)
            };

            // Add eviction callback for statistics
            cacheEntryOptions.RegisterPostEvictionCallback((key, value, reason, state) =>
            {
                lock (_statsLock)
                {
                    _statistics.TotalEntries = Math.Max(0, _statistics.TotalEntries - 1);
                    if (value is byte[] data)
                    {
                        _statistics.TotalMemoryUsage = Math.Max(0, _statistics.TotalMemoryUsage - data.Length);
                    }
                }

                _logger.LogDebug("Cache entry evicted: {Key}, Reason: {Reason}", key, reason);
            });

            _memoryCache.Set(queryKey, serializedData, cacheEntryOptions);

            lock (_statsLock)
            {
                _statistics.TotalEntries++;
                _statistics.TotalMemoryUsage += serializedData.Length;
                _statistics.LastUpdated = DateTime.UtcNow;
            }

            _logger.LogDebug("Cached response for query key: {QueryKey}, Size: {Size} bytes, Expiration: {Expiration}",
                queryKey, serializedData.Length, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error caching response for key: {QueryKey}", queryKey);
        }
    }

    public async Task RemoveAsync(string queryKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryKey))
            return;

        try
        {
            _memoryCache.Remove(queryKey);
            _logger.LogDebug("Removed cache entry for key: {QueryKey}", queryKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error removing cache entry for key: {QueryKey}", queryKey);
        }
    }

    public string GenerateCacheKey(MotorcycleQueryRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        // Create a normalized representation of the request for consistent caching
        var keyData = new
        {
            Query = request.Query?.Trim().ToLowerInvariant(),
            Preferences = new
            {
                MaxResults = request.Preferences?.MaxResults ?? 10,
                IncludeWebSources = request.Preferences?.IncludeWebSources ?? false,
                IncludePDFSources = request.Preferences?.IncludePDFSources ?? false,
                MinRelevanceScore = request.Preferences?.MinRelevanceScore ?? 0.0f,
                PreferredSources = request.Preferences?.PreferredSources?.OrderBy(s => s).ToArray() ?? Array.Empty<string>()
            }
        };

        var keyJson = JsonSerializer.Serialize(keyData, _jsonOptions);
        var keyBytes = Encoding.UTF8.GetBytes(keyJson);
        var hashBytes = SHA256.HashData(keyBytes);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_memoryCache is MemoryCache mc)
            {
                mc.Clear();
            }

            lock (_statsLock)
            {
                _statistics = new CacheStatistics();
            }

            _logger.LogInformation("Cache cleared successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error clearing cache");
        }
    }

    public async Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        lock (_statsLock)
        {
            return new CacheStatistics
            {
                TotalRequests = _statistics.TotalRequests,
                CacheHits = _statistics.CacheHits,
                CacheMisses = _statistics.CacheMisses,
                TotalEntries = _statistics.TotalEntries,
                TotalMemoryUsage = _statistics.TotalMemoryUsage,
                LastUpdated = _statistics.LastUpdated
            };
        }
    }

    private byte[] SerializeResponse(MotorcycleQueryResponse response)
    {
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        // Apply compression if enabled and data is large enough
        if (_config.EnableCompression && jsonBytes.Length > _config.CompressionThreshold)
        {
            return CompressData(jsonBytes);
        }

        return jsonBytes;
    }

    private MotorcycleQueryResponse DeserializeResponse(byte[] data)
    {
        // Check if data is compressed (simple magic number check)
        var isCompressed = data.Length > 2 && data[0] == 0x1f && data[1] == 0x8b;

        var jsonBytes = isCompressed ? DecompressData(data) : data;
        var json = Encoding.UTF8.GetString(jsonBytes);

        return JsonSerializer.Deserialize<MotorcycleQueryResponse>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize cached response");
    }

    private byte[] CompressData(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionMode.Compress))
        {
            gzip.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private byte[] DecompressData(byte[] compressedData)
    {
        using var input = new MemoryStream(compressedData);
        using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private CacheItemPriority DetermineCachePriority(MotorcycleQueryResponse response)
    {
        // Prioritize responses with more sources and better metrics
        if (response.Sources?.Length > 5 && response.Metrics?.ProcessingTimeMs < 1000)
            return CacheItemPriority.High;

        if (response.Sources?.Length > 2)
            return CacheItemPriority.Normal;

        return CacheItemPriority.Low;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Configuration for caching behavior.
/// </summary>
public class CacheConfiguration
{
    public bool EnableCaching { get; set; } = true;
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan LongTermExpiration { get; set; } = TimeSpan.FromHours(24);
    public int MaxMemorySizeMB { get; set; } = 100;
    public bool EnableCompression { get; set; } = true;
    public int CompressionThreshold { get; set; } = 1024; // Compress if larger than 1KB
    public int MaxCacheEntries { get; set; } = 1000;
}
