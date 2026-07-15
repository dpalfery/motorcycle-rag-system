using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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

    private static (SigningKeyCache Cache, Mock<HttpMessageHandler> Handler) CreateCache()
    {
        var handler = new Mock<HttpMessageHandler>();
        var httpClient = handler.CreateClient();
        var cache = new SigningKeyCache(httpClient, NullLogger<SigningKeyCache>.Instance);
        return (cache, handler);
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
    public async Task RefreshKeysAsync_WhenCachedEntryHasExpired_FetchesFreshKeysAndReplacesCacheContents()
    {
        // Arrange
        var (cache, handler) = CreateCache();
        handler.SetupRequest(HttpMethod.Get, MetadataUrl)
            .ReturnsResponse(HttpStatusCode.OK, BuildMetadataJson(), "application/json");

        // First fetch returns "key-1"; force the cached entry to look expired, then verify a second
        // refresh fetches and swaps in "key-2".
        handler.SetupRequest(HttpMethod.Get, JwksUrl)
            .ReturnsResponse(HttpStatusCode.OK, BuildJwksJson("key-1"), "application/json");

        await cache.PreWarmCacheAsync(Issuer);
        cache.GetSigningKeys(Issuer).Select(k => k.KeyId).Should().ContainSingle().Which.Should().Be("key-1");

        ForceCacheEntryExpired(cache, Issuer);

        handler.SetupRequest(HttpMethod.Get, JwksUrl)
            .ReturnsResponse(HttpStatusCode.OK, BuildJwksJson("key-2"), "application/json");

        // Act
        await cache.PreWarmCacheAsync(Issuer);

        // Assert
        var refreshedKeys = cache.GetSigningKeys(Issuer).ToList();
        refreshedKeys.Should().ContainSingle();
        refreshedKeys[0].KeyId.Should().Be("key-2");
        handler.VerifyRequest(HttpMethod.Get, JwksUrl, Times.Exactly(2));
    }

    /// <summary>
    /// Uses reflection to directly manipulate the private key cache dictionary so that the cached
    /// entry's expiry is in the past, without waiting on the real 1-hour TTL.
    /// </summary>
    private static void ForceCacheEntryExpired(SigningKeyCache cache, string issuer)
    {
        var field = typeof(SigningKeyCache).GetField("_keyCache", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_keyCache field not found on SigningKeyCache.");

        var dictionaryType = field.FieldType;
        var dictionary = field.GetValue(cache)
            ?? throw new InvalidOperationException("_keyCache value was null.");

        var tryGetValue = dictionaryType.GetMethod("TryGetValue")!;
        var args = new object?[] { issuer, null };
        var found = (bool)tryGetValue.Invoke(dictionary, args)!;
        found.Should().BeTrue("the cache must already contain an entry for the issuer before forcing expiry");

        var tupleType = dictionaryType.GetGenericArguments()[1];
        // Named tuple element names (Keys, Expiry) are compiler metadata only — the runtime
        // ValueTuple<T1,T2> fields are Item1/Item2.
        var keysField = tupleType.GetField("Item1")!;
        var cachedKeys = keysField.GetValue(args[1]);

        var expiredEntry = Activator.CreateInstance(tupleType, cachedKeys, DateTime.UtcNow.AddHours(-1));

        var indexer = dictionaryType.GetProperty("Item")!;
        indexer.SetValue(dictionary, expiredEntry, [issuer]);
    }
}
