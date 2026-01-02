using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Caching;
using MotorcycleRAG.Domain.DTOs;

namespace MotorcycleRAG.PerformanceTests;

/// <summary>
/// Performance benchmarks for caching functionality.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class CachingPerformanceTests
{
    private IQueryCacheService _cacheService = null!;
    private MotorcycleQueryRequest _testRequest = null!;
    private MotorcycleQueryResponse _testResponse = null!;
    private string _cacheKey = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        services.Configure<CacheConfiguration>(config =>
        {
            config.EnableCaching = true;
            config.EnableCompression = true;
            config.CompressionThreshold = 1024;
            config.MaxMemorySizeMB = 100;
        });
        services.AddSingleton<IQueryCacheService, MemoryQueryCacheService>();

        var serviceProvider = services.BuildServiceProvider();
        _cacheService = serviceProvider.GetRequiredService<IQueryCacheService>();

        // Create test data
        _testRequest = new MotorcycleQueryRequest
        {
            Query = "What are the specifications for Honda CBR600RR?",
            UserId = "performance-test-user",
            Preferences = new SearchPreferences
            {
                MaxResults = 10,
                IncludeWebSources = true,
                IncludePDFSources = false
            }
        };

        _testResponse = new MotorcycleQueryResponse
        {
            QueryId = Guid.NewGuid().ToString(),
            Response = "The Honda CBR600RR is a 600cc supersport motorcycle with an inline-4 engine producing 118 horsepower. It features a lightweight aluminum frame, advanced suspension, and aerodynamic bodywork designed for track performance.",
            Sources = new[]
            {
                new SearchResult
                {
                    Id = "honda-cbr600rr-specs",
                    Content = "Honda CBR600RR specifications: 599cc inline-4 engine, 118hp @ 14,000rpm, 64.5Nm torque, 194kg dry weight",
                    RelevanceScore = 0.95f,
                    Source = new SearchResultSource
                    {
                        Type = SearchSource.VectorDatabase,
                        DocumentId = "motorcycle-specs-001"
                    }
                }
            },
            Metrics = new QueryMetrics
            {
                ProcessingTimeMs = 1250,
                ResultsFound = 1,
                EstimatedCost = 0.0025m
            }
        };

        _cacheKey = _cacheService.GenerateCacheKey(_testRequest);
    }

    [Benchmark]
    public string GenerateCacheKey()
    {
        return _cacheService.GenerateCacheKey(_testRequest);
    }

    [Benchmark]
    public async Task CacheSet()
    {
        await _cacheService.SetAsync(_cacheKey, _testResponse, TimeSpan.FromMinutes(30));
    }

    [Benchmark]
    public async Task CacheGet()
    {
        await _cacheService.GetAsync(_cacheKey);
    }

    [Benchmark]
    public async Task CacheSetAndGet()
    {
        await _cacheService.SetAsync(_cacheKey, _testResponse, TimeSpan.FromMinutes(30));
        var result = await _cacheService.GetAsync(_cacheKey);
        return result;
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    [Arguments(1000)]
    public async Task CacheBulkOperations(int operationCount)
    {
        var tasks = new List<Task>();
        
        for (int i = 0; i < operationCount; i++)
        {
            var key = $"{_cacheKey}-{i}";
            tasks.Add(_cacheService.SetAsync(key, _testResponse, TimeSpan.FromMinutes(30)));
        }
        
        await Task.WhenAll(tasks);
        
        tasks.Clear();
        
        for (int i = 0; i < operationCount; i++)
        {
            var key = $"{_cacheKey}-{i}";
            tasks.Add(_cacheService.GetAsync(key));
        }
        
        await Task.WhenAll(tasks);
    }
}

/// <summary>
/// Unit tests for caching performance validation.
/// </summary>
public class CachingPerformanceValidationTests
{
    private readonly IQueryCacheService _cacheService;

    public CachingPerformanceValidationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        services.Configure<CacheConfiguration>(config =>
        {
            config.EnableCaching = true;
            config.EnableCompression = true;
            config.CompressionThreshold = 1024;
            config.MaxMemorySizeMB = 100;
        });
        services.AddSingleton<IQueryCacheService, MemoryQueryCacheService>();

        var serviceProvider = services.BuildServiceProvider();
        _cacheService = serviceProvider.GetRequiredService<IQueryCacheService>();
    }

    [Fact]
    public async Task CacheOperations_ShouldMeetPerformanceTargets()
    {
        // Arrange
        var request = new MotorcycleQueryRequest
        {
            Query = "Performance test query",
            UserId = "test-user"
        };

        var response = new MotorcycleQueryResponse
        {
            QueryId = Guid.NewGuid().ToString(),
            Response = "Test response for performance validation",
            Sources = new[]
            {
                new SearchResult
                {
                    Id = "test-result",
                    Content = "Test content for performance measurement",
                    RelevanceScore = 0.9f
                }
            }
        };

        var cacheKey = _cacheService.GenerateCacheKey(request);

        // Act & Assert - Cache Set Performance
        var setStopwatch = System.Diagnostics.Stopwatch.StartNew();
        await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(30));
        setStopwatch.Stop();

        setStopwatch.ElapsedMilliseconds.Should().BeLessThan(100, "Cache set should complete within 100ms");

        // Act & Assert - Cache Get Performance
        var getStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var cachedResponse = await _cacheService.GetAsync(cacheKey);
        getStopwatch.Stop();

        getStopwatch.ElapsedMilliseconds.Should().BeLessThan(50, "Cache get should complete within 50ms");
        cachedResponse.Should().NotBeNull();
        cachedResponse!.QueryId.Should().Be(response.QueryId);
    }

    [Fact]
    public async Task CacheKeyGeneration_ShouldBeConsistent()
    {
        // Arrange
        var request1 = new MotorcycleQueryRequest
        {
            Query = "Honda CBR600RR specifications",
            UserId = "user1",
            Preferences = new SearchPreferences { MaxResults = 10 }
        };

        var request2 = new MotorcycleQueryRequest
        {
            Query = "Honda CBR600RR specifications",
            UserId = "user2", // Different user
            Preferences = new SearchPreferences { MaxResults = 10 }
        };

        // Act
        var key1 = _cacheService.GenerateCacheKey(request1);
        var key2 = _cacheService.GenerateCacheKey(request2);

        // Assert
        key1.Should().Be(key2, "Cache keys should be the same for identical queries regardless of user");
        key1.Should().HaveLength(64, "Cache key should be a 64-character SHA256 hash");
    }

    [Fact]
    public async Task CacheStatistics_ShouldTrackOperations()
    {
        // Arrange
        var request = new MotorcycleQueryRequest
        {
            Query = "Statistics test query",
            UserId = "stats-user"
        };

        var response = new MotorcycleQueryResponse
        {
            QueryId = Guid.NewGuid().ToString(),
            Response = "Statistics test response"
        };

        var cacheKey = _cacheService.GenerateCacheKey(request);

        // Act
        await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(30));
        await _cacheService.GetAsync(cacheKey); // Hit
        await _cacheService.GetAsync("non-existent-key"); // Miss

        var stats = await _cacheService.GetStatisticsAsync();

        // Assert
        stats.TotalRequests.Should().BeGreaterThan(0);
        stats.CacheHits.Should().BeGreaterThan(0);
        stats.CacheMisses.Should().BeGreaterThan(0);
        stats.HitRatio.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(1);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(500)]
    public async Task ConcurrentCacheOperations_ShouldHandleLoad(int concurrentOperations)
    {
        // Arrange
        var tasks = new List<Task>();
        var responses = new List<MotorcycleQueryResponse>();

        for (int i = 0; i < concurrentOperations; i++)
        {
            responses.Add(new MotorcycleQueryResponse
            {
                QueryId = Guid.NewGuid().ToString(),
                Response = $"Concurrent test response {i}"
            });
        }

        // Act - Concurrent Sets
        var setStopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        for (int i = 0; i < concurrentOperations; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                var key = $"concurrent-test-{index}";
                await _cacheService.SetAsync(key, responses[index], TimeSpan.FromMinutes(30));
            }));
        }

        await Task.WhenAll(tasks);
        setStopwatch.Stop();

        // Act - Concurrent Gets
        tasks.Clear();
        var getStopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        for (int i = 0; i < concurrentOperations; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                var key = $"concurrent-test-{index}";
                return await _cacheService.GetAsync(key);
            }));
        }

        var results = await Task.WhenAll(tasks.Cast<Task<MotorcycleQueryResponse?>>());
        getStopwatch.Stop();

        // Assert
        setStopwatch.ElapsedMilliseconds.Should().BeLessThan(concurrentOperations * 10, 
            "Concurrent cache sets should complete efficiently");
        
        getStopwatch.ElapsedMilliseconds.Should().BeLessThan(concurrentOperations * 5, 
            "Concurrent cache gets should complete efficiently");
        
        results.Should().AllSatisfy(result => result.Should().NotBeNull());
    }
}

/// <summary>
/// Program entry point for running benchmarks.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "benchmark")
        {
            BenchmarkRunner.Run<CachingPerformanceTests>();
        }
        else
        {
            Console.WriteLine("Run with 'benchmark' argument to execute performance benchmarks.");
            Console.WriteLine("Otherwise, run as normal unit tests.");
        }
    }
}
