using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Moq.Contrib.HttpClient;
using MotorcycleRAG.API.Extensions;
using Xunit;

namespace MotorcycleRAG.UnitTests.Presentation.API.Extensions;

/// <summary>
/// Covers <see cref="SigningKeyCache"/>'s HTTP-backed JWKS fetch (<c>FetchSigningKeysAsync</c>) and
/// caching/expiry logic (<c>RefreshKeysAsync</c>), both private and exercised indirectly through the
/// internal <c>PreWarmCacheAsync</c> / <c>GetSigningKeys</c> entry points.
/// </summary>
public sealed class SigningKeyCacheTests
{
    private const string Issuer = "https://issuer.example.com";
    private const string MetadataUrl = "https://issuer.example.com/.well-known/openid-configuration";
    private const string JwksUrl = "https://issuer.example.com/discovery/v2.0/keys";

    private static string BuildMetadataJson(string jwksUri = JwksUrl) =>
        $$"""{"issuer":"{{Issuer}}","jwks_uri":"{{jwksUri}}"}""";

    private static string BuildJwksJson(params string[] keyIds)
    {
        var keys = string.Join(",", keyIds.Select(kid =>
            $$"""{"kty":"RSA","kid":"{{kid}}","use":"sig","n":"xGOr-H7A-PWG","e":"AQAB"}"""));
        return $$"""{"keys":[{{keys}}]}""";
    }

    private static (SigningKeyCache Cache, Mock<HttpMessageHandler> Handler) CreateCache(TimeProvider? timeProvider = null)
    {
        var handler = new Mock<HttpMessageHandler>();
        var httpClient = handler.CreateClient();
        var cache = new SigningKeyCache(
            httpClient,
            NullLogger<SigningKeyCache>.Instance,
            timeProvider ?? TimeProvider.System);
        return (cache, handler);
    }

    [Fact]
    public void Constructor_WhenDependencyIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object);

        // Act
        var nullClient = () => new SigningKeyCache(null!, NullLogger<SigningKeyCache>.Instance);
        var nullLogger = () => new SigningKeyCache(httpClient, null!);
        var nullTimeProvider = () => new SigningKeyCache(httpClient, NullLogger<SigningKeyCache>.Instance, null!);

        // Assert
        nullClient.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        nullLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        nullTimeProvider.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    [Fact]
    public async Task PreWarmCacheAsync_WhenMetadataAndJwksSucceed_PopulatesCacheWithParsedKeys()
    {
        // Arrange
        var (cache, handler) = CreateCache();
        handler.SetupRequest(HttpMethod.Get, MetadataUrl)
            .ReturnsResponse(HttpStatusCode.OK, BuildMetadataJson(), "application/json");
        handler.SetupRequest(HttpMethod.Get, JwksUrl)
            .ReturnsResponse(HttpStatusCode.OK, BuildJwksJson("key-1"), "application/json");

        // Act
        await cache.PreWarmCacheAsync(Issuer);
        var keys = cache.GetSigningKeys(Issuer).ToList();

        // Assert
        keys.Should().ContainSingle();
        keys[0].KeyId.Should().Be("key-1");
    }

    [Fact]
    public async Task PreWarmCacheAsync_WhenMetadataRequestFails_CacheRemainsEmpty()
    {
        // Arrange
        var (cache, handler) = CreateCache();
        handler.SetupRequest(HttpMethod.Get, MetadataUrl)
            .ReturnsResponse(HttpStatusCode.ServiceUnavailable);

        // Act
        await cache.PreWarmCacheAsync(Issuer);
        var keys = cache.GetSigningKeys(Issuer).ToList();

        // Assert
        keys.Should().BeEmpty();
        handler.VerifyRequest(HttpMethod.Get, JwksUrl, Times.Never());
    }

    [Fact]
    public async Task PreWarmCacheAsync_WhenJwksUriMissingFromMetadata_CacheRemainsEmpty()
    {
        // Arrange
        var (cache, handler) = CreateCache();
        handler.SetupRequest(HttpMethod.Get, MetadataUrl)
            .ReturnsResponse(HttpStatusCode.OK, """{"issuer":"https://issuer.example.com"}""", "application/json");

        // Act
        await cache.PreWarmCacheAsync(Issuer);
        var keys = cache.GetSigningKeys(Issuer).ToList();

        // Assert
        keys.Should().BeEmpty();
        handler.VerifyRequest(HttpMethod.Get, JwksUrl, Times.Never());
    }

    [Fact]
    public async Task PreWarmCacheAsync_WhenJwksUriIsBlank_CacheRemainsEmpty()
    {
        var (cache, handler) = CreateCache();
        handler.SetupRequest(HttpMethod.Get, MetadataUrl)
            .ReturnsResponse(HttpStatusCode.OK, """{"jwks_uri":""}""", "application/json");

        await cache.PreWarmCacheAsync(Issuer);

        cache.GetSigningKeys(Issuer).Should().BeEmpty();
        handler.VerifyRequest(HttpMethod.Get, JwksUrl, Times.Never());
    }

    [Fact]
    public async Task PreWarmCacheAsync_WarmsBothIssuers_AndEmptyIssuerReturnsNoKeys()
    {
        var (cache, handler) = CreateCache();
        const string externalIssuer = "https://external.example.com";
        const string externalMetadataUrl = "https://external.example.com/.well-known/openid-configuration";
        const string externalJwksUrl = "https://external.example.com/keys";
        handler.SetupRequest(HttpMethod.Get, MetadataUrl).ReturnsResponse(HttpStatusCode.OK, BuildMetadataJson(), "application/json");
        handler.SetupRequest(HttpMethod.Get, JwksUrl).ReturnsResponse(HttpStatusCode.OK, BuildJwksJson("workforce-key"), "application/json");
        handler.SetupRequest(HttpMethod.Get, externalMetadataUrl).ReturnsResponse(HttpStatusCode.OK, $$"""{"jwks_uri":"{{externalJwksUrl}}"}""", "application/json");
        handler.SetupRequest(HttpMethod.Get, externalJwksUrl).ReturnsResponse(HttpStatusCode.OK, BuildJwksJson("external-key"), "application/json");

        await cache.PreWarmCacheAsync(Issuer, externalIssuer);

        cache.GetSigningKeys(externalIssuer).Should().ContainSingle().Which.KeyId.Should().Be("external-key");
        cache.GetSigningKeys("").Should().BeEmpty();
        cache.QueueRefresh(" ");
    }

    [Fact]
    public async Task PreWarmCacheAsync_WhenJwksRequestFails_CacheRemainsEmpty()
    {
        // Arrange
        var (cache, handler) = CreateCache();
        handler.SetupRequest(HttpMethod.Get, MetadataUrl)
            .ReturnsResponse(HttpStatusCode.OK, BuildMetadataJson(), "application/json");
        handler.SetupRequest(HttpMethod.Get, JwksUrl)
            .ReturnsResponse(HttpStatusCode.NotFound);

        // Act
        await cache.PreWarmCacheAsync(Issuer);
        var keys = cache.GetSigningKeys(Issuer).ToList();

        // Assert
        keys.Should().BeEmpty();
    }

    [Fact]
    public async Task PreWarmCacheAsync_CalledTwiceWhileCacheStillValid_DoesNotRefetchFromNetwork()
    {
        // Arrange
        var (cache, handler) = CreateCache();
        handler.SetupRequest(HttpMethod.Get, MetadataUrl)
            .ReturnsResponse(HttpStatusCode.OK, BuildMetadataJson(), "application/json");
        handler.SetupRequest(HttpMethod.Get, JwksUrl)
            .ReturnsResponse(HttpStatusCode.OK, BuildJwksJson("key-1"), "application/json");

        // Act — first call populates the cache with a 1-hour TTL; the second call should short-circuit
        // via RefreshKeysAsync's "double-check cache after acquiring semaphore" branch.
        await cache.PreWarmCacheAsync(Issuer);
        await cache.PreWarmCacheAsync(Issuer);

        // Assert
        handler.VerifyRequest(HttpMethod.Get, MetadataUrl, Times.Once());
        handler.VerifyRequest(HttpMethod.Get, JwksUrl, Times.Once());
    }

    [Fact]
    public async Task GetSigningKeys_WhenCacheExpires_ReturnsStaleKeysAndRefreshesReplacement()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero));
        var (cache, handler) = CreateCache(timeProvider);
        handler.SetupRequest(HttpMethod.Get, MetadataUrl)
            .ReturnsResponse(HttpStatusCode.OK, BuildMetadataJson(), "application/json");
        handler.SetupRequest(HttpMethod.Get, JwksUrl)
            .ReturnsResponse(HttpStatusCode.OK, BuildJwksJson("key-1"), "application/json");
        await cache.PreWarmCacheAsync(Issuer);
        timeProvider.Advance(TimeSpan.FromHours(1).Add(TimeSpan.FromTicks(1)));
        handler.SetupRequest(HttpMethod.Get, JwksUrl)
            .ReturnsResponse(HttpStatusCode.OK, BuildJwksJson("key-2"), "application/json");

        // Act
        var staleKeys = cache.GetSigningKeys(Issuer).ToList();
        await cache.PreWarmCacheAsync(Issuer);
        var refreshedKeys = cache.GetSigningKeys(Issuer).ToList();

        // Assert
        staleKeys.Should().ContainSingle().Which.KeyId.Should().Be("key-1");
        refreshedKeys.Should().ContainSingle().Which.KeyId.Should().Be("key-2");
    }
}
