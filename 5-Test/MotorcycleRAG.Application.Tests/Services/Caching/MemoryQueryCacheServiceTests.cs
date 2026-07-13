using System.Collections.ObjectModel;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Services.Caching;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Services.Caching;

public sealed class MemoryQueryCacheServiceTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10_000_000 });

    [Fact]
    public async Task SetGetRemoveAndClearAsync_TrackCacheStatistics()
    {
        using var sut = CreateSut();
        var response = CreateResponse("first", sourceCount: 3, processingTimeMs: 100);

        await sut.SetAsync("key", response, TimeSpan.FromMinutes(5));
        var cached = await sut.GetAsync("key");
        await sut.RemoveAsync("key");
        var missing = await sut.GetAsync("key");
        await Task.Delay(100); // Give the background thread time to run the eviction callback
        var statistics = await sut.GetStatisticsAsync();

        cached.Should().BeEquivalentTo(response);
        missing.Should().BeNull();
        statistics.TotalRequests.Should().Be(2);
        statistics.CacheHits.Should().Be(1);
        statistics.CacheMisses.Should().Be(1);
        statistics.TotalEntries.Should().Be(0);
        statistics.TotalMemoryUsage.Should().Be(0);

        await sut.ClearAsync();
        (await sut.GetStatisticsAsync()).Should().BeEquivalentTo(new CacheStatistics(), options =>
            options.Excluding(info => info.LastUpdated));
    }

    [Fact]
    public async Task SetAndGetAsync_WithCompression_RoundTripsResponse()
    {
        using var sut = CreateSut(enableCompression: true, compressionThreshold: 1);
        var response = CreateResponse(new string('x', 500), sourceCount: 6, processingTimeMs: 50);

        await sut.SetAsync("compressed", response, TimeSpan.FromMinutes(5));

        var cached = await sut.GetAsync("compressed");

        cached.Should().BeEquivalentTo(response);
    }

    [Fact]
    public async Task GetSetAndRemoveAsync_WithInvalidArguments_AreNoOps()
    {
        using var sut = CreateSut();

        (await sut.GetAsync(" ")).Should().BeNull();
        await sut.SetAsync("", CreateResponse("ignored"), TimeSpan.FromMinutes(1));
        await sut.SetAsync("key", null!, TimeSpan.FromMinutes(1));
        await sut.RemoveAsync(" ");

        var statistics = await sut.GetStatisticsAsync();
        statistics.TotalRequests.Should().Be(0);
        statistics.TotalEntries.Should().Be(0);
    }

    [Fact]
    public async Task CacheOperations_WhenTheCacheFails_ReturnSafeDefaults()
    {
        using var sut = CreateSut(new ThrowingMemoryCache());

        (await sut.GetAsync("key")).Should().BeNull();
        await sut.SetAsync("key", CreateResponse("response"), TimeSpan.FromMinutes(1));
        await sut.RemoveAsync("key");

        var statistics = await sut.GetStatisticsAsync();
        statistics.TotalRequests.Should().Be(1);
        statistics.CacheMisses.Should().Be(0);
        statistics.TotalEntries.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_WithInvalidCachedPayload_ReturnsNullAndCountsTheHit()
    {
        _cache.Set("invalid", new byte[] { 0x1f, 0x8b }, new MemoryCacheEntryOptions { Size = 2 });
        using var sut = CreateSut();

        (await sut.GetAsync("invalid")).Should().BeNull();

        var statistics = await sut.GetStatisticsAsync();
        statistics.CacheHits.Should().Be(1);
        statistics.CacheMisses.Should().Be(0);
    }

    [Fact]
    public async Task ClearAsync_WithNonMemoryCache_ResetsStatistics()
    {
        using var sut = CreateSut(new NoOpMemoryCache());
        await sut.GetAsync("key");

        await sut.ClearAsync();

        (await sut.GetStatisticsAsync()).TotalRequests.Should().Be(0);
    }

    [Fact]
    public async Task ClearAsync_WhenAnEvictionLogFails_SwallowsTheFailure()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10_000_000 });
        using var sut = new MemoryQueryCacheService(
            cache,
            new ThrowOnDebugLogger(),
            Options.Create(new CacheConfiguration()));
        await sut.SetAsync("key", CreateResponse("response"), TimeSpan.FromMinutes(1));

        var act = () => sut.ClearAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void GenerateCacheKey_NormalizesQueryAndPreferredSourceOrdering()
    {
        using var sut = CreateSut();
        var first = CreateRequest("  adventure bike  ", ["web", "pdf"]);
        var equivalent = CreateRequest("ADVENTURE BIKE", ["pdf", "web"]);
        var different = CreateRequest("touring bike", ["pdf", "web"]);

        sut.GenerateCacheKey(first).Should().Be(sut.GenerateCacheKey(equivalent));
        sut.GenerateCacheKey(first).Should().NotBe(sut.GenerateCacheKey(different));
        sut.Invoking(service => service.GenerateCacheKey(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullDependencies_ThrowsArgumentNullException()
    {
        var options = Options.Create(new CacheConfiguration());

        ((Action)(() => new MemoryQueryCacheService(null!, NullLogger<MemoryQueryCacheService>.Instance, options)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new MemoryQueryCacheService(_cache, null!, options)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new MemoryQueryCacheService(_cache, NullLogger<MemoryQueryCacheService>.Instance, null!)))
            .Should().Throw<ArgumentNullException>();
    }

    public void Dispose() => _cache.Dispose();

    private MemoryQueryCacheService CreateSut(
        IMemoryCache? memoryCache = null,
        bool enableCompression = false,
        int compressionThreshold = 1024) => new(
        memoryCache ?? _cache,
        NullLogger<MemoryQueryCacheService>.Instance,
        Options.Create(new CacheConfiguration
        {
            EnableCompression = enableCompression,
            CompressionThreshold = compressionThreshold,
        }));

    private static MotorcycleQueryResponse CreateResponse(string text, int sourceCount = 1, int processingTimeMs = 200) => new()
    {
        QueryId = "query-id",
        Response = text,
        Sources = Enumerable.Range(0, sourceCount)
            .Select(index => new SearchResult
            {
                Id = index.ToString(),
                Content = text,
                Source = new SearchSource { SourceName = "unit" },
            })
            .ToArray(),
        Metrics = new QueryMetrics { ProcessingTimeMs = processingTimeMs },
    };

    private static MotorcycleQueryRequest CreateRequest(string query, string[] sources) => new()
    {
        Query = query,
        Preferences = new SearchPreferences { PreferredSources = new Collection<string>(sources) },
    };

    private class NoOpMemoryCache : IMemoryCache
    {
        public ICacheEntry CreateEntry(object key) => throw new NotSupportedException();
        public void Dispose() { }
        public void Remove(object key) { }
        public bool TryGetValue(object key, out object? value)
        {
            value = null;
            return false;
        }
    }

    private sealed class ThrowingMemoryCache : IMemoryCache
    {
        public ICacheEntry CreateEntry(object key) => throw new InvalidOperationException("cache unavailable");
        public void Dispose() { }
        public void Remove(object key) => throw new InvalidOperationException("cache unavailable");
        public bool TryGetValue(object key, out object? value)
        {
            value = null;
            throw new InvalidOperationException("cache unavailable");
        }
    }

    private sealed class ThrowOnDebugLogger : ILogger<MemoryQueryCacheService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Debug)
            {
                throw new InvalidOperationException("logger unavailable");
            }
        }
    }
}
