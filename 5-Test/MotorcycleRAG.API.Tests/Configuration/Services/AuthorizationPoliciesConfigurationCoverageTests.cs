using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Configuration.Services;

namespace MotorcycleRAG.UnitTests.Presentation.API.Configuration.Services;

public sealed class AuthorizationPoliciesConfigurationCoverageTests
{
    [Fact]
    public void AddMotorcycleRagAuthorization_NullArguments_Throws()
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = CreateEnvironment("Production");
        var services = new ServiceCollection();

        ((Action)(() => AuthorizationPoliciesConfiguration.AddMotorcycleRagAuthorization(null!, configuration, environment)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => services.AddMotorcycleRagAuthorization(null!, environment)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => services.AddMotorcycleRagAuthorization(configuration, null!)))
            .Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("Chat", "scp", "chat", true)]
    [InlineData("Chat", "scp", "read", false)]
    [InlineData("User", ClaimTypes.Role, "User", true)]
    [InlineData("User", ClaimTypes.Role, "Viewer", false)]
    [InlineData("Viewer", ClaimTypes.Role, "Viewer", true)]
    [InlineData("Viewer", ClaimTypes.Role, "User", false)]
    public async Task Policies_EnforceExpectedClaim(string policyName, string claimType, string claimValue, bool expected)
    {
        using var provider = BuildProvider("Production", "admin-client", "processor-client");
        var policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy(policyName)!;
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(claimType, claimValue)], "coverage"));

        (await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy.Requirements)).Succeeded
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("Production", "File.Upload.All", "processor-client", true)]
    [InlineData("Production", "File.Upload.All", "wrong-client", false)]
    [InlineData("Testing", "File.Upload.All", null, true)]
    [InlineData("Testing", "other-role", null, false)]
    public async Task LocalProcessorPolicy_EnforcesProductionClientIsolationAndTestingRoleFallback(
        string environmentName,
        string role,
        string? clientId,
        bool expected)
    {
        using var provider = BuildProvider(environmentName, "admin-client", "processor-client");
        var policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy("mcr-api-local-processor")!;
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (clientId is not null)
            claims.Add(new Claim("azp", clientId));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "coverage"));

        (await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy.Requirements)).Succeeded
            .Should().Be(expected);
    }

    [Fact]
    public async Task LocalProcessorPolicy_TestingHeaderFallback_SucceedsWithoutUploadRole()
    {
        using var provider = BuildProvider("Testing", "admin-client", "processor-client");
        var policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy("mcr-api-local-processor")!;
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("X-Test-Auth", "mcr-api-local-processor")], "coverage"));

        (await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy.Requirements)).Succeeded
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task AdminPolicy_TestingHeaderFallback_StillRequiresAdminRole(bool includeAdminRole, bool expected)
    {
        using var provider = BuildProvider("Testing", null, "processor-client");
        var policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy("mcr-api-admin")!;
        var claims = new List<Claim> { new("X-Test-Auth", "mcr-api-admin") };
        if (includeAdminRole)
            claims.Add(new Claim(ClaimTypes.Role, "mcr-api-admin"));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "coverage"));

        (await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy.Requirements)).Succeeded
            .Should().Be(expected);
    }

    [Fact]
    public async Task AdminPolicy_ProductionWithoutConfiguredClientId_DeniesOtherwiseValidCaller()
    {
        using var provider = BuildProvider("Production", null, "processor-client");
        var policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy("mcr-api-admin")!;
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("scp", "admin"),
            new Claim(ClaimTypes.Role, "mcr-api-admin"),
            new Claim("azp", "admin-client")], "coverage"));

        (await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy.Requirements)).Succeeded
            .Should().BeFalse();
    }

    [Fact]
    public async Task AdminPolicy_TestingWithoutScopeOrHeader_DeniesAdminRole()
    {
        using var provider = BuildProvider("Testing", "admin-client", "processor-client");
        var policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy("mcr-api-admin")!;
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "mcr-api-admin")], "coverage"));

        (await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy.Requirements)).Succeeded
            .Should().BeFalse();
    }

    [Fact]
    public async Task AdminPolicy_TestingWithAdminScope_SucceedsWithoutTestHeader()
    {
        using var provider = BuildProvider("Testing", "admin-client", "processor-client");
        var policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy("mcr-api-admin")!;
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("scp", "admin"),
            new Claim(ClaimTypes.Role, "mcr-api-admin")], "coverage"));

        (await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy.Requirements)).Succeeded
            .Should().BeTrue();
    }

    [Fact]
    public async Task LocalProcessorPolicy_ProductionWithoutConfiguredClientId_DeniesOtherwiseValidCaller()
    {
        using var provider = BuildProvider("Production", "admin-client", null);
        var policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy("mcr-api-local-processor")!;
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "File.Upload.All"),
            new Claim("azp", "processor-client")], "coverage"));

        (await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy.Requirements)).Succeeded
            .Should().BeFalse();
    }

    private static ServiceProvider BuildProvider(string environmentName, string? adminClientId, string? localProcessorClientId)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:AdminClientId"] = adminClientId,
            ["AzureAd:LocalProcessorClientId"] = localProcessorClientId
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMotorcycleRagAuthorization(configuration, CreateEnvironment(environmentName));
        return services.BuildServiceProvider();
    }

    private static IWebHostEnvironment CreateEnvironment(string name)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(item => item.EnvironmentName).Returns(name);
        return environment.Object;
    }
}
