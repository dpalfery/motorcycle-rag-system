using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.IntegrationTests;

/// <summary>
/// Test authentication handler for integration tests
/// Supports simulating authenticated users with various roles
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder, new SystemClock())
    {
    }

    private class SystemClock : Microsoft.AspNetCore.Authentication.ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check if the request has the test auth header
        if (!Request.Headers.ContainsKey("X-Test-Auth"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var authHeaderValue = Request.Headers["X-Test-Auth"].ToString();
        var roles = authHeaderValue.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Email, "test@example.com")
        };

        // Add roles from header
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Trim()));
        }

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Extension methods for configuring test authentication
/// </summary>
public static class TestAuthExtensions
{
    /// <summary>
    /// Creates an HTTP client with test authentication for DataAdmin role
    /// </summary>
    public static HttpClient CreateDataAdminClient(this WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "DataAdmin");
        return client;
    }

    /// <summary>
    /// Creates an HTTP client with test authentication for Admin role
    /// </summary>
    public static HttpClient CreateAdminClient(this WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "Admin");
        return client;
    }

    /// <summary>
    /// Creates an HTTP client with test authentication for User role
    /// </summary>
    public static HttpClient CreateUserClient(this WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "User");
        return client;
    }

    /// <summary>
    /// Creates an HTTP client with test authentication for multiple roles
    /// </summary>
    public static HttpClient CreateClientWithRoles(this WebApplicationFactory<Program> factory, params string[] roles)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", string.Join(",", roles));
        return client;
    }
}
