using FluentAssertions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MotorcycleRag.WebUI.BFF.Configuration.Services;
using Xunit;

namespace MotorcycleRag.WebUI.BFF.Tests.Configuration.Services;

public class CorsServiceConfigurationTests
{
    private static CorsOptions BuildCorsOptions(string[]? origins = null)
    {
        var configDict = new Dictionary<string, string?>();
        if (origins is not null)
        {
            for (var i = 0; i < origins.Length; i++)
                configDict[$"Cors:AllowedOrigins:{i}"] = origins[i];
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var services = new ServiceCollection();
        services.AddBffCors(config);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<CorsOptions>>().Value;
    }

    [Fact]
    public void AddBffCors_RegistersAllowFrontendPolicy()
    {
        var options = BuildCorsOptions(["https://app.example.com"]);

        options.GetPolicy("AllowFrontend").Should().NotBeNull();
    }

    [Fact]
    public void AddBffCors_WithConfiguredOrigins_AppliesOrigins()
    {
        var options = BuildCorsOptions(["https://app.example.com", "https://admin.example.com"]);

        var policy = options.GetPolicy("AllowFrontend")!;
        policy.Origins.Should().Contain("https://app.example.com");
        policy.Origins.Should().Contain("https://admin.example.com");
    }

    [Fact]
    public void AddBffCors_NoConfiguredOrigins_FallsBackToLocalhost()
    {
        var options = BuildCorsOptions(null); // no config

        var policy = options.GetPolicy("AllowFrontend")!;
        policy.Origins.Should().Contain("http://localhost:3000");
    }

    [Fact]
    public void AddBffCors_AllowsCredentials()
    {
        var options = BuildCorsOptions(["https://app.example.com"]);

        options.GetPolicy("AllowFrontend")!.SupportsCredentials.Should().BeTrue();
    }

    [Fact]
    public void AddBffCors_AllowsOnlyExplicitMethods()
    {
        var options = BuildCorsOptions(["https://app.example.com"]);

        var policy = options.GetPolicy("AllowFrontend")!;
        policy.Methods.Should().BeEquivalentTo(new[] { "GET", "POST", "PUT", "DELETE" });
        policy.Methods.Should().NotContain("PATCH");
        policy.Methods.Should().NotContain("TRACE");
    }

    [Fact]
    public void AddBffCors_AllowsExplicitHeadersOnly()
    {
        var options = BuildCorsOptions(["https://app.example.com"]);

        var policy = options.GetPolicy("AllowFrontend")!;
        policy.Headers.Should().BeEquivalentTo(
            new[] { "Content-Type", "Authorization", "X-Requested-With" });
    }

    [Fact]
    public void AddBffCors_ExposesContentDispositionHeader()
    {
        var options = BuildCorsOptions(["https://app.example.com"]);

        var policy = options.GetPolicy("AllowFrontend")!;
        policy.ExposedHeaders.Should().Contain("Content-Disposition");
    }

    [Fact]
    public void AddBffCors_SetsPreflightMaxAgeTo5Minutes()
    {
        var options = BuildCorsOptions(["https://app.example.com"]);

        var policy = options.GetPolicy("AllowFrontend")!;
        policy.PreflightMaxAge.Should().Be(TimeSpan.FromMinutes(5));
    }
}
