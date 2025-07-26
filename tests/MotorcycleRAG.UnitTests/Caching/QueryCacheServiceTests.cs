using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Core.Models;
using MotorcycleRAG.Infrastructure.Caching;
using Xunit;

namespace MotorcycleRAG.UnitTests.Caching;

public class QueryCacheServiceTests : IDisposable
{
    private readonly Mock<ILogger<QueryCacheService>> _mockLogger;
    private readonly IMemoryCache _memoryCache;
    private readonly QueryCacheService _cacheService;
    private readonly CacheConfiguration _cacheConfig;

    public QueryCacheServiceTests()
    {
        _mockLogger = new Mock<ILogger<QueryCacheService>>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _cacheConfig = new CacheConfiguration
        {
            DefaultCacheDuration = TimeSpan.FromMinutes(30),
            MaxCacheEntries = 1000,
            MemoryLimitMB = 100,
            EnableCompression = true
        };
        
        var options = Options.Create(_cacheConfig);
        _cacheService = new QueryCacheService(_memoryCache, _mockLogger.Object, options);
    }

    [Fact]
    public async Task GetCachedResultsAsync_WhenCacheHit_ReturnsResults()
    {
        // Arrange
        var cacheKey = "test_key";
        var expectedResults = new[]
        {
            new SearchResult
            {
                Id = "1",
                Content = "Test content",
                RelevanceScore = 0.8f,
                Source = new SearchSource { AgentType = SearchAgentType.VectorSearch, SourceName = "test" }
            }
        };

        await _cacheService.SetCachedResultsAsync(cacheKey, expectedResults, TimeSpan.FromMinutes(5));

        // Act
        var result = await _cacheService.GetCachedResultsAsync(cacheKey);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("1", result[0].Id);
        Assert.Equal("Test content", result[0].Content);
        Assert.Equal(0.8f, result[0].RelevanceScore);
    }

    [Fact]
    public async Task GetCachedResultsAsync_WhenCacheMiss_ReturnsNull()
    {
        // Arrange
        var cacheKey = "nonexistent_key";

        // Act
        var result = await _cacheService.GetCachedResultsAsync(cacheKey);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SetCachedResultsAsync_StoresResultsSuccessfully()
    {
        // Arrange
        var cacheKey = "test_key";
        var results = new[]
        {
            new SearchResult
            {
                Id = "1",
                Content = "Test content",
                RelevanceScore = 0.9f,
                Source = new SearchSource { AgentType = SearchAgentType.WebSearch, SourceName = "web" }
            }
        };

        // Act
        await _cacheService.SetCachedResultsAsync(cacheKey, results, TimeSpan.FromMinutes(10));
        var retrievedResults = await _cacheService.GetCachedResultsAsync(cacheKey);

        // Assert
        Assert.NotNull(retrievedResults);
        Assert.Single(retrievedResults);
        Assert.Equal("1", retrievedResults[0].Id);
        Assert.Equal("Test content", retrievedResults[0].Content);
    }

    [Fact]
    public void GenerateCacheKey_WithSameInputs_GeneratesSameKey()
    {
        // Arrange
        var query = "motorcycle specifications";
        var options = new SearchOptions
        {
            MaxResults = 10,
            MinRelevanceScore = 0.5f,
            IncludeMetadata = true
        };
        var agentType = SearchAgentType.VectorSearch;

        // Act
        var key1 = _cacheService.GenerateCacheKey(query, options, agentType);
        var key2 = _cacheService.GenerateCacheKey(query, options, agentType);

        // Assert
        Assert.Equal(key1, key2);
        Assert.StartsWith("query_cache:VectorSearch:", key1);
    }

    [Fact]
    public void GenerateCacheKey_WithDifferentInputs_GeneratesDifferentKeys()
    {
        // Arrange
        var query1 = "motorcycle specifications";
        var query2 = "motorcycle maintenance";
        var options = new SearchOptions { MaxResults = 10 };
        var agentType = SearchAgentType.VectorSearch;

        // Act
        var key1 = _cacheService.GenerateCacheKey(query1, options, agentType);
        var key2 = _cacheService.GenerateCacheKey(query2, options, agentType);

        // Assert
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GetCacheStatistics_ReturnsCorrectStatistics()
    {
        // Arrange & Act
        var initialStats = _cacheService.GetCacheStatistics();

        // Assert
        Assert.Equal(0, initialStats.TotalRequests);
        Assert.Equal(0, initialStats.CacheHits);
        Assert.Equal(0, initialStats.CacheMisses);
        Assert.Equal(0.0, initialStats.HitRatio);
    }

    [Fact]
    public async Task GetCacheStatistics_AfterOperations_UpdatesCorrectly()
    {
        // Arrange
        var cacheKey = "test_key";
        var results = new[]
        {
            new SearchResult
            {
                Id = "1",
                Content = "Test",
                RelevanceScore = 0.5f,
                Source = new SearchSource { AgentType = SearchAgentType.VectorSearch, SourceName = "test" }
            }
        };

        // Act
        await _cacheService.SetCachedResultsAsync(cacheKey, results, TimeSpan.FromMinutes(5));
        await _cacheService.GetCachedResultsAsync(cacheKey); // Hit
        await _cacheService.GetCachedResultsAsync("missing_key"); // Miss

        var stats = _cacheService.GetCacheStatistics();

        // Assert
        Assert.Equal(2, stats.TotalRequests);
        Assert.Equal(1, stats.CacheHits);
        Assert.Equal(1, stats.CacheMisses);
        Assert.Equal(0.5, stats.HitRatio);
    }

    [Fact]
    public async Task InvalidateCacheAsync_WithWildcard_ClearsAllEntries()
    {
        // Arrange
        var cacheKey = "test_key";
        var results = new[]
        {
            new SearchResult
            {
                Id = "1",
                Content = "Test",
                RelevanceScore = 0.5f,
                Source = new SearchSource { AgentType = SearchAgentType.VectorSearch, SourceName = "test" }
            }
        };

        await _cacheService.SetCachedResultsAsync(cacheKey, results, TimeSpan.FromMinutes(5));

        // Act
        await _cacheService.InvalidateCacheAsync("*");
        var retrievedResults = await _cacheService.GetCachedResultsAsync(cacheKey);

        // Assert
        Assert.Null(retrievedResults);
    }

    [Fact]
    public void GenerateCacheKey_WithNullOrEmptyQuery_HandlesGracefully()
    {
        // Arrange
        var options = new SearchOptions { MaxResults = 10 };
        var agentType = SearchAgentType.VectorSearch;

        // Act & Assert
        var key1 = _cacheService.GenerateCacheKey("", options, agentType);
        var key2 = _cacheService.GenerateCacheKey("   ", options, agentType);

        Assert.NotNull(key1);
        Assert.NotNull(key2);
        Assert.StartsWith("query_cache:VectorSearch:", key1);
        Assert.StartsWith("query_cache:VectorSearch:", key2);
    }

    [Fact]
    public async Task GetCachedResultsAsync_WithExpiredEntry_ReturnsNull()
    {
        // Arrange
        var cacheKey = "test_key";
        var results = new[]
        {
            new SearchResult
            {
                Id = "1",
                Content = "Test",
                RelevanceScore = 0.5f,
                Source = new SearchSource { AgentType = SearchAgentType.VectorSearch, SourceName = "test" }
            }
        };

        // Act
        await _cacheService.SetCachedResultsAsync(cacheKey, results, TimeSpan.FromMilliseconds(1));
        await Task.Delay(50); // Wait for expiration
        var retrievedResults = await _cacheService.GetCachedResultsAsync(cacheKey);

        // Assert
        Assert.Null(retrievedResults);
    }

    public void Dispose()
    {
        _memoryCache?.Dispose();
    }
}