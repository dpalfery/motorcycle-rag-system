using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.API.Configuration.Services;
using System.Security.Claims;
using Xunit;

namespace MotorcycleRAG.UnitTests.Presentation.API.Configuration.Services;

public class AuthorizationPoliciesConfigurationTests
{
    private static (IServiceProvider Provider, AuthorizationOptions AuthOptions) BuildWith(
        string? adminClientId = "test-admin-client",
        string envName = "Production")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:AdminClientId"] = adminClientId,
            })
            .Build();

        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(envName);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMotorcycleRagAuthorization(configuration, env.Object);

        var provider = services.BuildServiceProvider();
        var authOptions = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        return (provider, authOptions);
    }

    // ── Policy existence ────────────────────────────────────────────────────

    [Theory]
    [InlineData("mcr-api-admin")]
    [InlineData("Read")]
    [InlineData("Chat")]
    [InlineData("User")]
    [InlineData("Viewer")]
    [InlineData("mcr-api-manuals-view")]
    public void AddMotorcycleRagAuthorization_RegistersAllExpectedPolicies(string policyName)
    {
        var (_, authOptions) = BuildWith();

        authOptions.GetPolicy(policyName).Should().NotBeNull(
            because: $"policy '{policyName}' should be registered");
    }

    [Fact]
    public void AddMotorcycleRagAuthorization_DefaultAndFallbackPoliciesRequireAuthentication()
    {
        var (_, authOptions) = BuildWith();

        authOptions.DefaultPolicy.Should().NotBeNull();
        authOptions.FallbackPolicy.Should().NotBeNull();
    }

    // ── Testing environment dummy client ID ─────────────────────────────────

    [Fact]
    public void AddMotorcycleRagAuthorization_TestingEnvironment_NullAdminClientId_DoesNotThrow()
    {
        // In Testing env, a dummy client ID is injected if AdminClientId isn't set
        var act = () => BuildWith(adminClientId: null, envName: "Testing");
        act.Should().NotThrow();
    }

    // ── Admin policy assertion logic ────────────────────────────────────────

    [Fact]
    public async Task AdminPolicy_Production_RequiresScopeRoleAndClient()
    {
        var (provider, authOptions) = BuildWith(adminClientId: "admin-client-id");
        var policyEvaluator = provider.GetRequiredService<IAuthorizationService>();
        var policy = authOptions.GetPolicy("mcr-api-admin")!;

        // User with all three: scope, role, correct azp
        var claims = new[]
        {
            new Claim("scp", "admin"),
            new Claim(System.Security.Claims.ClaimTypes.Role, "mcr-api-admin"),
            new Claim("azp", "admin-client-id"),
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var result = await policyEvaluator.AuthorizeAsync(user, null, policy.Requirements);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AdminPolicy_Production_MissingScope_Fails()
    {
        var (provider, authOptions) = BuildWith(adminClientId: "admin-client-id");
        var policyEvaluator = provider.GetRequiredService<IAuthorizationService>();
        var policy = authOptions.GetPolicy("mcr-api-admin")!;

        var claims = new[]
        {
            // No "admin" scope
            new Claim(System.Security.Claims.ClaimTypes.Role, "mcr-api-admin"),
            new Claim("azp", "admin-client-id"),
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var result = await policyEvaluator.AuthorizeAsync(user, null, policy.Requirements);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AdminPolicy_Production_WrongClient_Fails()
    {
        var (provider, authOptions) = BuildWith(adminClientId: "admin-client-id");
        var policyEvaluator = provider.GetRequiredService<IAuthorizationService>();
        var policy = authOptions.GetPolicy("mcr-api-admin")!;

        var claims = new[]
        {
            new Claim("scp", "admin"),
            new Claim(System.Security.Claims.ClaimTypes.Role, "mcr-api-admin"),
            new Claim("azp", "wrong-client-id"), // wrong azp
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var result = await policyEvaluator.AuthorizeAsync(user, null, policy.Requirements);

        result.Succeeded.Should().BeFalse();
    }

    // ── Read policy ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadPolicy_WithReadScope_Succeeds()
    {
        var (provider, authOptions) = BuildWith();
        var svc = provider.GetRequiredService<IAuthorizationService>();
        var policy = authOptions.GetPolicy("Read")!;

        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("scp", "read")], "Test"));

        (await svc.AuthorizeAsync(user, null, policy.Requirements)).Succeeded
            .Should().BeTrue();
    }

    [Fact]
    public async Task ReadPolicy_WithoutReadScope_Fails()
    {
        var (provider, authOptions) = BuildWith();
        var svc = provider.GetRequiredService<IAuthorizationService>();
        var policy = authOptions.GetPolicy("Read")!;

        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("scp", "chat")], "Test")); // wrong scope

        (await svc.AuthorizeAsync(user, null, policy.Requirements)).Succeeded
            .Should().BeFalse();
    }

    // ── ManualsView policy ──────────────────────────────────────────────────

    [Theory]
    [InlineData("User")]
    [InlineData("Viewer")]
    [InlineData("mcr-api-admin")]
    public async Task ManualsViewPolicy_AllowedRoles_Succeeds(string role)
    {
        var (provider, authOptions) = BuildWith();
        var svc = provider.GetRequiredService<IAuthorizationService>();
        var policy = authOptions.GetPolicy("mcr-api-manuals-view")!;

        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(System.Security.Claims.ClaimTypes.Role, role)], "Test"));

        (await svc.AuthorizeAsync(user, null, policy.Requirements)).Succeeded
            .Should().BeTrue(because: $"role '{role}' should be allowed by ManualsView");
    }

    [Fact]
    public async Task ManualsViewPolicy_UnknownRole_Fails()
    {
        var (provider, authOptions) = BuildWith();
        var svc = provider.GetRequiredService<IAuthorizationService>();
        var policy = authOptions.GetPolicy("mcr-api-manuals-view")!;

        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(System.Security.Claims.ClaimTypes.Role, "Guest")], "Test"));

        (await svc.AuthorizeAsync(user, null, policy.Requirements)).Succeeded
            .Should().BeFalse();
    }
}
