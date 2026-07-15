using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using MotorcycleRAG.API;
using MotorcycleRAG.API.Extensions;
using System.Security.Claims;

namespace MotorcycleRAG.UnitTests.Presentation.API.Extensions;

public class AuthenticationServiceExtensionsTests
{
    [Fact]
    public void AddDualIssuerJwtBearer_RejectsMissingRequiredSettings()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddAuthentication();

        var noIssuer = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Audience"] = "audience"
        }).Build();
        var noAudience = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Issuers:Workforce"] = "https://issuer.example.test"
        }).Build();

        Action missingIssuer = () => InvokeAddDualIssuerJwtBearer(builder, noIssuer);
        Action missingAudience = () => InvokeAddDualIssuerJwtBearer(builder, noAudience);

        missingIssuer.Should().Throw<TargetInvocationException>().Which.InnerException.Should().BeOfType<InvalidOperationException>();
        missingAudience.Should().Throw<TargetInvocationException>().Which.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void AddDualIssuerJwtBearer_ExternalIssuerAndAdditionalAudiences_ConfiguresAllAliases()
    {
        const string clientId = "9221a8e3-4d10-45d4-ae8c-76acd4a5d631";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Issuers:Workforce"] = "https://workforce.example.test",
            ["Authentication:Issuers:ExternalId"] = "https://external.example.test",
            ["Authentication:Audience"] = $"api://{clientId}",
            ["Authentication:AdditionalAudiences:0"] = "additional-audience",
            ["Authentication:AdditionalAudiences:1"] = clientId
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();

        InvokeAddDualIssuerJwtBearer(services.AddAuthentication(), configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        options.TokenValidationParameters.ValidIssuers.Should().BeEquivalentTo("https://workforce.example.test", "https://external.example.test");
        options.TokenValidationParameters.ValidAudiences.Should().BeEquivalentTo(clientId, $"api://{clientId}", "additional-audience", "api://motorcyclerag-api");
    }

    [Fact]
    public void BuildValidAudiences_IgnoresBlankValuesAndRetainsNamedAudience()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:ClientId"] = "  ",
            ["Authentication:AdditionalAudiences:0"] = " ",
            ["Authentication:AdditionalAudiences:1"] = "api://not-a-guid"
        }).Build();
        var extensionType = typeof(MotorcycleRAG.API.Program).Assembly.GetType(
            "MotorcycleRAG.API.Extensions.AuthenticationServiceExtensions", throwOnError: true)!;
        var method = extensionType.GetMethod("BuildValidAudiences", BindingFlags.Static | BindingFlags.NonPublic)!;

        var audiences = ((IReadOnlyCollection<string>)method.Invoke(null, [configuration, " "])!).ToArray();

        audiences.Should().BeEquivalentTo("api://motorcyclerag-api", "api://not-a-guid");
    }

    [Fact]
    public async Task ConfigureJwtBearerOptions_ResolvesIssuerKeysAndHandlesMissingIssuerOrKey()
    {
        const string issuer = "https://issuer.example.test";
        using var client = new HttpClient(new StaticDiscoveryHandler(issuer));
        var cache = new SigningKeyCache(client, NullLogger<SigningKeyCache>.Instance);
        await cache.PreWarmCacheAsync(issuer);

        var extensionType = typeof(MotorcycleRAG.API.Program).Assembly.GetType(
            "MotorcycleRAG.API.Extensions.AuthenticationServiceExtensions", throwOnError: true)!;
        var configure = extensionType.GetMethod("ConfigureJwtBearerOptions", BindingFlags.Static | BindingFlags.NonPublic)!;
        var options = new JwtBearerOptions();
        configure.Invoke(null, [options, cache, issuer, null, new[] { "audience" }]);
        var resolve = options.TokenValidationParameters.IssuerSigningKeyResolver!;
        var token = new JsonWebToken("eyJhbGciOiJub25lIn0.eyJpc3MiOiJodHRwczovL2lzc3Vlci5leGFtcGxlLnRlc3QifQ.");

        resolve(null!, null!, null!, null!).Should().BeEmpty();
        resolve(null!, token, null!, null!).Should().ContainSingle().Which.KeyId.Should().Be("key-1");
        resolve(null!, token, "key-1", null!).Should().ContainSingle().Which.KeyId.Should().Be("key-1");
        resolve(null!, token, "missing", null!).Should().BeEmpty();
    }

    [Fact]
    public async Task ConfigureJwtBearerOptions_InvokesAuthenticationEvents_WithAndWithoutClaims()
    {
        var extensionType = typeof(MotorcycleRAG.API.Program).Assembly.GetType(
            "MotorcycleRAG.API.Extensions.AuthenticationServiceExtensions", throwOnError: true)!;
        var configure = extensionType.GetMethod("ConfigureJwtBearerOptions", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var client = new HttpClient(new StaticDiscoveryHandler("https://issuer.example.test"));
        var cache = new SigningKeyCache(client, NullLogger<SigningKeyCache>.Instance);
        var options = new JwtBearerOptions();
        configure.Invoke(null, [options, cache, "https://issuer.example.test", null, new[] { "audience" }]);
        var scheme = new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler));
        var noLoggerContext = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };

        await options.Events.AuthenticationFailed(new AuthenticationFailedContext(noLoggerContext, scheme, options)
        {
            Exception = new InvalidOperationException("test")
        });
        await options.Events.TokenValidated(new TokenValidatedContext(noLoggerContext, scheme, options));

        var services = new ServiceCollection();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();
        var claimsContext = new DefaultHttpContext { RequestServices = provider };
        await options.Events.TokenValidated(new TokenValidatedContext(claimsContext, scheme, options)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("iss", "issuer"), new Claim("sub", "subject")]))
        });
    }

    [Fact]
    public void AddDualIssuerJwtBearer_GuidAudience_AlsoAcceptsNamedApplicationIdUri()
    {
        // Arrange
        const string clientId = "9221a8e3-4d10-45d4-ae8c-76acd4a5d631";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Authentication:Issuers:Workforce", "https://login.microsoftonline.com/tenant-123/v2.0" },
                { "Authentication:Audience", clientId },
                { "AzureAd:ClientId", clientId }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var authenticationBuilder = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

        // Act
        InvokeAddDualIssuerJwtBearer(authenticationBuilder, configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
        var options = optionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);

        // Assert
        options.TokenValidationParameters.ValidAudiences.Should().NotBeNull();
        options.TokenValidationParameters.ValidAudiences.Should().BeEquivalentTo(new[]
        {
            clientId,
            $"api://{clientId}",
            "api://motorcyclerag-api"
        });
    }

    private static AuthenticationBuilder InvokeAddDualIssuerJwtBearer(
        AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        var extensionType = typeof(MotorcycleRAG.API.Program).Assembly.GetType(
            "MotorcycleRAG.API.Extensions.AuthenticationServiceExtensions",
            throwOnError: true)!;
        var method = extensionType.GetMethod(
            "AddDualIssuerJwtBearer",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var result = method.Invoke(null, [builder, configuration, NullLogger.Instance]);
        result.Should().NotBeNull();
        result.Should().BeOfType<AuthenticationBuilder>();

        return (AuthenticationBuilder)result!;
    }

    private sealed class StaticDiscoveryHandler(string issuer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith("openid-configuration", StringComparison.Ordinal)
                ? $$"""{"jwks_uri":"{{issuer}}/keys"}"""
                : """{"keys":[{"kty":"RSA","kid":"key-1","use":"sig","n":"xGOr-H7A-PWG","e":"AQAB"}]}""";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }
}
