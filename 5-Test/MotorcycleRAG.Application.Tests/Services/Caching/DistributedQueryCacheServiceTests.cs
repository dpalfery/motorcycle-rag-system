using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Services.Caching;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Services.Caching;

public sealed class DistributedQueryCacheServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task GetAsync_WhenCacheContainsPlainResponse_ReturnsDeserializedResponse()
    {
        var response = CreateResponse("plain response");
        var cache = CreateCache();
        cache.Setup(distributedCache => distributedCache.GetAsync(
                "motorcycle-rag:query:query-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Serialize(response));
        var sut = CreateSut(cache.Object);

        var result = await sut.GetAsync("query-key");

        result.Should().BeEquivalentTo(response);
    }

    [Fact]
    public async Task GetAsync_WhenCacheMissesOrKeyIsInvalid_ReturnsNullWithoutCallingCache()
    {
        var cache = CreateCache();
        cache.Setup(distributedCache => distributedCache.GetAsync(
                "motorcycle-rag:query:query-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        var sut = CreateSut(cache.Object);

        var missing = await sut.GetAsync("query-key");
        var invalid = await sut.GetAsync(" ");

        missing.Should().BeNull();
        invalid.Should().BeNull();
        cache.Verify(distributedCache => distributedCache.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_WhenCachedPayloadIsCompressed_ReturnsDeserializedResponse()
    {
        var response = CreateResponse(new string('x', 500));
        var cache = CreateCache();
        cache.Setup(distributedCache => distributedCache.GetAsync(
                "motorcycle-rag:query:compressed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Compress(Serialize(response)));
        var sut = CreateSut(cache.Object, enableCompression: true, compressionThreshold: 1);

        var result = await sut.GetAsync("compressed");

        result.Should().BeEquivalentTo(response);
    }

    [Fact]
    public async Task GetAsync_WhenCacheOrPayloadFails_ReturnsNull()
    {
        var cache = CreateCache();
        cache.Setup(distributedCache => distributedCache.GetAsync(
                "motorcycle-rag:query:failure", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache unavailable"));
        cache.Setup(distributedCache => distributedCache.GetAsync(
                "motorcycle-rag:query:invalid", It.IsAny<CancellationToken>()))
            .ReturnsAsync([0x1f, 0x8b]);
        var sut = CreateSut(cache.Object);

        var cacheFailure = await sut.GetAsync("failure");
        var invalidPayload = await sut.GetAsync("invalid");

        cacheFailure.Should().BeNull();
        invalidPayload.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_WhenResponseIsValid_SerializesResponseAndAppliesExpirationOptions()
    {
        var response = CreateResponse("response");
        var cache = CreateCache();
        byte[]? cachedData = null;
        DistributedCacheEntryOptions? cachedOptions = null;
        cache.Setup(distributedCache => distributedCache.SetAsync(
                "motorcycle-rag:query:query-key",
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, data, options, _) =>
            {
                cachedData = data;
                cachedOptions = options;
            })
            .Returns(Task.CompletedTask);
        var sut = CreateSut(cache.Object);

        await sut.SetAsync("query-key", response, TimeSpan.FromMinutes(10));

        cachedData.Should().NotBeNull();
        JsonSerializer.Deserialize<MotorcycleQueryResponse>(cachedData!, JsonOptions).Should().BeEquivalentTo(response);
        cachedOptions!.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromMinutes(10));
        cachedOptions.SlidingExpiration.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task SetAsync_WhenCompressionIsEnabledAndThresholdIsExceeded_WritesGzipPayload()
    {
        var cache = CreateCache();
        byte[]? cachedData = null;
        cache.Setup(distributedCache => distributedCache.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, data, _, _) => cachedData = data)
            .Returns(Task.CompletedTask);
        var sut = CreateSut(cache.Object, enableCompression: true, compressionThreshold: 1);

        await sut.SetAsync("compressed", CreateResponse(new string('x', 500)), TimeSpan.FromMinutes(1));

        cachedData.Should().NotBeNull();
        cachedData![..2].Should().Equal(new byte[] { 0x1f, 0x8b });
    }

    [Fact]
    public async Task SetAsync_WhenArgumentsAreInvalidOrCacheFails_DoesNotPropagateFailure()
    {
        var cache = CreateCache();
        cache.Setup(distributedCache => distributedCache.SetAsync(
                "motorcycle-rag:query:failure",
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache unavailable"));
        var sut = CreateSut(cache.Object);

        var act = async () =>
        {
            await sut.SetAsync(" ", CreateResponse("ignored"), TimeSpan.FromMinutes(1));
            await sut.SetAsync("key", null!, TimeSpan.FromMinutes(1));
            await sut.SetAsync("failure", CreateResponse("response"), TimeSpan.FromMinutes(1));
        };

        await act.Should().NotThrowAsync();
        cache.Verify(distributedCache => distributedCache.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_WhenKeyIsValid_RemovesPrefixedEntryAndSwallowsCacheFailure()
    {
        var cache = CreateCache();
        cache.Setup(distributedCache => distributedCache.RemoveAsync(
                "motorcycle-rag:query:query-key", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        cache.Setup(distributedCache => distributedCache.RemoveAsync(
                "motorcycle-rag:query:failure", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache unavailable"));
        var sut = CreateSut(cache.Object);

        await sut.RemoveAsync("query-key");
        var act = () => sut.RemoveAsync("failure");
        await sut.RemoveAsync(" ");

        await act.Should().NotThrowAsync();
        cache.Verify(distributedCache => distributedCache.RemoveAsync(
            "motorcycle-rag:query:query-key", It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(distributedCache => distributedCache.RemoveAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ClearAndGetStatisticsAsync_ReturnSupportedDefaults()
    {
        var sut = CreateSut(CreateCache().Object);

        await sut.ClearAsync();
        var statistics = await sut.GetStatisticsAsync();

        statistics.TotalRequests.Should().Be(0);
        statistics.CacheHits.Should().Be(0);
        statistics.CacheMisses.Should().Be(0);
        statistics.TotalEntries.Should().Be(0);
        statistics.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GenerateCacheKey_WhenRequestsAreEquivalent_NormalizesQueryAndSourceOrder()
    {
        var sut = CreateSut(CreateCache().Object);
        var first = CreateRequest("  adventure bike  ", ["web", "pdf"]);
        var equivalent = CreateRequest("ADVENTURE BIKE", ["pdf", "web"]);
        var different = CreateRequest("touring bike", ["pdf", "web"]);

        sut.GenerateCacheKey(first).Should().Be(sut.GenerateCacheKey(equivalent));
        sut.GenerateCacheKey(first).Should().NotBe(sut.GenerateCacheKey(different));
        sut.Invoking(service => service.GenerateCacheKey(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WhenDependenciesAreNull_ThrowsArgumentNullException()
    {
        var options = Options.Create(new CacheConfiguration());
        var cache = CreateCache().Object;

        Assert.Throws<ArgumentNullException>(() => new DistributedQueryCacheService(null!, NullLogger<DistributedQueryCacheService>.Instance, options));
        Assert.Throws<ArgumentNullException>(() => new DistributedQueryCacheService(cache, null!, options));
        Assert.Throws<ArgumentNullException>(() => new DistributedQueryCacheService(cache, NullLogger<DistributedQueryCacheService>.Instance, null!));
    }

    private static Mock<IDistributedCache> CreateCache() => new(MockBehavior.Strict);

    private static DistributedQueryCacheService CreateSut(
        IDistributedCache cache,
        bool enableCompression = false,
        int compressionThreshold = 1024) => new(
        cache,
        NullLogger<DistributedQueryCacheService>.Instance,
        Options.Create(new CacheConfiguration
        {
            EnableCompression = enableCompression,
            CompressionThreshold = compressionThreshold,
        }));

    private static MotorcycleQueryResponse CreateResponse(string text) => new()
    {
        QueryId = "query-id",
        Response = text,
        Sources =
        [
            new SearchResult
            {
                Id = "source-id",
                Content = text,
                Source = new SearchSource { SourceName = "unit" },
            },
        ],
        Metrics = new QueryMetrics { ProcessingTimeMs = 200 },
    };

    private static MotorcycleQueryRequest CreateRequest(string query, string[] sources) => new()
    {
        Query = query,
        Preferences = new SearchPreferences { PreferredSources = new Collection<string>(sources) },
    };

    private static byte[] Serialize(MotorcycleQueryResponse response) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, JsonOptions));

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }
}
