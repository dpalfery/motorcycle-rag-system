using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MotorcycleRag.WebUI.BFF.Configuration.Services;
using Xunit;

namespace MotorcycleRag.WebUI.BFF.Tests.Configuration.Services;

public class AuthenticationServiceConfigurationTests {
    private static readonly string[] RequiredScopes = ["openid", "profile", "email", "api://motorcyclerag-api/read", "api://motorcyclerag-api/chat"];

    [Fact]
    public void AddBffAuthentication_RegistersExpectedSchemes() {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AzureAd:Instance", "https://login.microsoftonline.com/" },
                { "AzureAd:TenantId", "tenant-id" },
                { "AzureAd:ClientId", "client-id" },
                { "AzureAd:ClientSecret", "client-secret" },
                { "ReverseProxy:Clusters:api-cluster:Destinations:destination1:Address", "https://localhost:7215" }
            })
            .Build();

        // Act
        services.AddBffAuthentication(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var authOptions = serviceProvider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        authOptions.DefaultScheme.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
        authOptions.DefaultChallengeScheme.Should().Be(OpenIdConnectDefaults.AuthenticationScheme);
    }

    [Fact]
    public void AddBffAuthentication_ConfiguresCookieOptions() {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AzureAd:Instance", "https://login.microsoftonline.com/" },
                { "AzureAd:TenantId", "tenant-id" },
                { "AzureAd:ClientId", "client-id" },
                { "AzureAd:ClientSecret", "client-secret" },
                { "ReverseProxy:Clusters:api-cluster:Destinations:destination1:Address", "https://localhost:7215" }
            })
            .Build();

        // Act
        services.AddBffAuthentication(configuration);
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptionsSnapshot<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        // Assert
        options.Cookie.HttpOnly.Should().BeTrue();
        options.Cookie.Name.Should().Be("MotorcycleRAG");
        options.SlidingExpiration.Should().BeTrue();
    }

    [Fact]
    public void AddBffAuthentication_ConfiguresOidcOptions() {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AzureAd:Instance", "https://login.microsoftonline.com/" },
                { "AzureAd:TenantId", "tenant-id" },
                { "AzureAd:ClientId", "client-id" },
                { "AzureAd:ClientSecret", "client-secret" },
                { "ReverseProxy:Clusters:api-cluster:Destinations:destination1:Address", "https://localhost:7215" }
            })
            .Build();

        // Act
        services.AddBffAuthentication(configuration);
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptionsSnapshot<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        // Assert
        options.Authority.Should().Be("https://login.microsoftonline.com/tenant-id/v2.0");
        options.ClientId.Should().Be("client-id");
        options.Scope.Should().Contain(RequiredScopes);
        options.UsePkce.Should().BeTrue();
    }

    [Fact]
    public void AddBffAuthentication_RegistersApprovalStatusHttpClient() {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AzureAd:Instance", "https://login.microsoftonline.com/" },
                { "AzureAd:TenantId", "tenant-id" },
                { "AzureAd:ClientId", "client-id" },
                { "AzureAd:ClientSecret", "client-secret" },
                { "ReverseProxy:Clusters:api-cluster:Destinations:destination1:Address", "https://localhost:7215" }
            })
            .Build();

        services.AddBffAuthentication(configuration);
        var serviceProvider = services.BuildServiceProvider();

        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        using var client = httpClientFactory.CreateClient(AuthenticationServiceConfiguration.ApprovalStatusHttpClientName);

        client.BaseAddress.Should().Be(new Uri("https://localhost:7215/"));
    }

    [Fact]
    public void AddBffAuthentication_WithTrailingSlashAddress_KeepsAbsoluteUri() {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AzureAd:Instance", "https://login.microsoftonline.com/" },
                { "AzureAd:TenantId", "tenant-id" },
                { "AzureAd:ClientId", "client-id" },
                { "AzureAd:ClientSecret", "client-secret" },
                { "ReverseProxy:Clusters:api-cluster:Destinations:destination1:Address", "https://localhost:7215/" }
            })
            .Build();

        services.AddBffAuthentication(configuration);
        using var serviceProvider = services.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        using var client = httpClientFactory.CreateClient(AuthenticationServiceConfiguration.ApprovalStatusHttpClientName);

        client.BaseAddress.Should().Be(new Uri("https://localhost:7215/"));
    }

    [Fact]
    public void AddBffAuthentication_WithPathAddressWithoutTrailingSlash_AddsTrailingSlash() {
        var services = new ServiceCollection();
        var configuration = CreateValidConfiguration("https://localhost:7215/api");

        services.AddBffAuthentication(configuration);
        using var serviceProvider = services.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        using var client = httpClientFactory.CreateClient(AuthenticationServiceConfiguration.ApprovalStatusHttpClientName);

        client.BaseAddress.Should().Be(new Uri("https://localhost:7215/api/"));
    }

    [Fact]
    public void AddBffAuthentication_WithoutApiClusterAddress_LeavesClientBaseAddressNull() {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AzureAd:Instance", "https://login.microsoftonline.com/" },
                { "AzureAd:TenantId", "tenant-id" },
                { "AzureAd:ClientId", "client-id" },
                { "AzureAd:ClientSecret", "client-secret" }
            })
            .Build();

        services.AddBffAuthentication(configuration);
        using var serviceProvider = services.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        using var client = httpClientFactory.CreateClient(AuthenticationServiceConfiguration.ApprovalStatusHttpClientName);

        client.BaseAddress.Should().BeNull();
    }

    [Fact]
    public void AddBffAuthentication_WithoutClientSecret_ThrowsWhenResolvingOidcOptions() {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AzureAd:Instance", "https://login.microsoftonline.com/" },
                { "AzureAd:TenantId", "tenant-id" },
                { "AzureAd:ClientId", "client-id" },
                { "ReverseProxy:Clusters:api-cluster:Destinations:destination1:Address", "https://localhost:7215" }
            })
            .Build();

        services.AddBffAuthentication(configuration);
        using var serviceProvider = services.BuildServiceProvider();

        var act = () => serviceProvider.GetRequiredService<IOptionsSnapshot<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Client Secret is not configured*");
    }

    [Fact]
    public async Task AddBffAuthentication_OnRedirectToIdentityProvider_UpgradesHttpToHttps() {
        var services = new ServiceCollection();
        var configuration = CreateValidConfiguration();
        services.AddBffAuthentication(configuration);
        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptionsSnapshot<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        var httpContext = new DefaultHttpContext();
        var message = new OpenIdConnectMessage {
            RedirectUri = "http://ui.example/signin-oidc"
        };
        var redirectContext = new RedirectContext(
            httpContext,
            new AuthenticationScheme(OpenIdConnectDefaults.AuthenticationScheme, null, typeof(OpenIdConnectHandler)),
            options,
            new AuthenticationProperties()) {
            ProtocolMessage = message
        };

        await options.Events.OnRedirectToIdentityProvider(redirectContext);

        message.RedirectUri.Should().Be("https://ui.example/signin-oidc");
    }

    [Fact]
    public async Task AddBffAuthentication_OnRemoteFailure_RedirectsToSignInError() {
        var services = new ServiceCollection();
        var configuration = CreateValidConfiguration();
        services.AddBffAuthentication(configuration);
        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptionsSnapshot<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        var failureContext = new RemoteFailureContext(
            httpContext,
            new AuthenticationScheme(OpenIdConnectDefaults.AuthenticationScheme, null, typeof(OpenIdConnectHandler)),
            options,
            new Exception("oidc failed"));

        await options.Events.OnRemoteFailure(failureContext);

        httpContext.Response.Headers.Location.ToString().Should().Be("/signin?error=auth_failed");
    }

    private static IConfiguration CreateValidConfiguration(string apiClusterAddress = "https://localhost:7215") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AzureAd:Instance", "https://login.microsoftonline.com/" },
                { "AzureAd:TenantId", "tenant-id" },
                { "AzureAd:ClientId", "client-id" },
                { "AzureAd:ClientSecret", "client-secret" },
                { "ReverseProxy:Clusters:api-cluster:Destinations:destination1:Address", apiClusterAddress }
            })
            .Build();
}
