using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Configuration.Services;
using Xunit;

namespace MotorcycleRAG.UnitTests.Presentation.API.Configuration.Services;

public class CorsServiceConfigurationTests
{
    private static CorsPolicy GetPolicy(string? originsConfig)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins"] = originsConfig,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddRestrictedCors(config);
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CorsOptions>>().Value;

        // AddDefaultPolicy registers under the DefaultPolicyName (empty string)
        return options.GetPolicy(options.DefaultPolicyName)!;
    }

    [Fact]
    public void AddRestrictedCors_WithConfiguredOrigins_AppliesOrigins()
    {
        var policy = GetPolicy("https://app.example.com;https://admin.example.com");

        policy.Should().NotBeNull();
        policy.Origins.Should().Contain("https://app.example.com");
        policy.Origins.Should().Contain("https://admin.example.com");
    }

    [Fact]
    public void AddRestrictedCors_NoConfiguredOrigins_FallsBackToLocalhost()
    {
        var policy = GetPolicy(null);

        policy.Origins.Should().Contain("https://localhost:3000");
    }

    [Fact]
    public void AddRestrictedCors_AllowsCredentials()
    {
        var policy = GetPolicy("https://app.example.com");

        policy.SupportsCredentials.Should().BeTrue();
    }

    [Fact]
    public void AddRestrictedCors_AllowsOnlyWhitelistedMethods()
    {
        var policy = GetPolicy("https://app.example.com");

        policy.Methods.Should().BeEquivalentTo("GET", "POST", "PUT", "DELETE", "OPTIONS");
    }

    [Fact]
    public void AddRestrictedCors_AllowsOnlyWhitelistedHeaders()
    {
        var policy = GetPolicy("https://app.example.com");

        policy.Headers.Should().BeEquivalentTo(
            "Content-Type", "Authorization", "X-Requested-With");
    }

    [Fact]
    public void AddRestrictedCors_ExposesOnlyXTotalCount()
    {
        var policy = GetPolicy("https://app.example.com");

        policy.ExposedHeaders.Should().BeEquivalentTo("X-Total-Count");
    }

    [Fact]
    public void AddRestrictedCors_SetsPreflightMaxAgeTo10Minutes()
    {
        var policy = GetPolicy("https://app.example.com");

        policy.PreflightMaxAge.Should().Be(TimeSpan.FromSeconds(600));
    }

    [Fact]
    public void AddRestrictedCors_DoesNotAllowDangerousMethods()
    {
        var policy = GetPolicy("https://app.example.com");

        policy.Methods.Should().NotContain("PATCH");
        policy.Methods.Should().NotContain("TRACE");
        policy.Methods.Should().NotContain("CONNECT");
    }
}
