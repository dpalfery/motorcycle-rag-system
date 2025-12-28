using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MotorcycleRAG.Application.Caching;

/// <summary>
/// Distributed cache implementation using Redis for production scenarios with multiple instances.
/// </summary>
public class DistributedQueryCacheService : IQueryCacheService
{
    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<DistributedQueryCacheService> _logger;
    private readonly CacheConfiguration _config;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _keyPrefix;

    public DistributedQueryCacheService(
        IDistributedCache distributedCache,
        ILogger<DistributedQueryCacheService> logger,
        IOptions<CacheConfiguration> config)
    {
        _distributedCache = distributedCache ?? throw new ArgumentNullException(nameof(distributedCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));

        _keyPrefix = "motorcycle-rag:query:";
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        _logger.LogInformation("Distributed query cache service initialized with prefix: {KeyPrefix}", _keyPrefix);
    }

    public async Task<MotorcycleQueryResponse?> GetAsync(string queryKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryKey))
            return null;

        try
        {
            var fullKey = _keyPrefix + queryKey;
            var cachedData = await _distributedCache.GetAsync(fullKey, cancellationToken);

            if (cachedData != null)
            {
                var response = DeserializeResponse(cachedData);
                _logger.LogDebug("Distributed cache hit for query key: {QueryKey}", queryKey);
                return response;
            }

            _logger.LogDebug("Distributed cache miss for query key: {QueryKey}", queryKey);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrieving from distributed cache for key: {QueryKey}", queryKey);
            return null;
        }
    }

    public async Task SetAsync(string queryKey, MotorcycleQueryResponse response, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryKey) || response == null)
            return;

        try
        {
            var fullKey = _keyPrefix + queryKey;
            var serializedData = SerializeResponse(response);

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration,
                SlidingExpiration = TimeSpan.FromMinutes(15) // Extend expiration on access
            };

            await _distributedCache.SetAsync(fullKey, serializedData, options, cancellationToken);

            _logger.LogDebug("Cached response in distributed cache for query key: {QueryKey}, Size: {Size} bytes, Expiration: {Expiration}", 
                queryKey, serializedData.Length, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error caching response in distributed cache for key: {QueryKey}", queryKey);
        }
    }

    public async Task RemoveAsync(string queryKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryKey))
            return;

        try
        {
            var fullKey = _keyPrefix + queryKey;
            await _distributedCache.RemoveAsync(fullKey, cancellationToken);
            _logger.LogDebug("Removed distributed cache entry for key: {QueryKey}", queryKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error removing distributed cache entry for key: {QueryKey}", queryKey);
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
        // Note: IDistributedCache doesn't provide a clear method
        // This would need to be implemented using Redis-specific commands
        _logger.LogWarning("Clear operation not supported by IDistributedCache interface");
        await Task.CompletedTask;
    }

    public async Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        // Note: IDistributedCache doesn't provide statistics
        // This would need to be implemented using Redis-specific commands or external monitoring
        _logger.LogDebug("Statistics not available through IDistributedCache interface");
        
        return new CacheStatistics
        {
            LastUpdated = DateTime.UtcNow
        };
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
}
