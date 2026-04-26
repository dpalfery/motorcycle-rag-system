using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
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
}
