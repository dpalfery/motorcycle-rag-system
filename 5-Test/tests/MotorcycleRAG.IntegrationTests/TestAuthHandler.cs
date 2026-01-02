using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;


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
        : base(options, logger, encoder)
    {
        // TimeProvider is set in TestWebApplicationFactory when configuring the scheme
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
            var trimmedRole = role.Trim();
            claims.Add(new Claim(ClaimTypes.Role, trimmedRole));

            // Per Program.cs policies, admin-level roles also require the "admin_access" scope
            if (trimmedRole.EndsWith("Admin", StringComparison.OrdinalIgnoreCase))
            {
                if (!claims.Any(c => c.Type == "scp" && c.Value == "admin_access"))
                {
                    claims.Add(new Claim("scp", "admin_access"));
                }
            }
        }

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
