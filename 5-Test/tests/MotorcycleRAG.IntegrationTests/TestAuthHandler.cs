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
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via DI in TestWebApplicationFactory")]
internal class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
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
        if (!Request.Headers.TryGetValue("X-Test-Auth", out var headerValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var authHeaderValue = headerValues.ToString();
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

            // Per Program.cs policies, admin-level roles also require the "admin" scope
            if ((trimmedRole.Equals("mcr-api-admin", StringComparison.OrdinalIgnoreCase) || 
                 trimmedRole.EndsWith("Admin", StringComparison.OrdinalIgnoreCase)) &&
                !claims.Any(c => c.Type == "scp" && c.Value == "admin"))
            {
                claims.Add(new Claim("scp", "admin"));
                
                // Also add azp (Authorized Party) claim for admin isolation checks
                if (!claims.Any(c => c.Type == "azp"))
                {
                    claims.Add(new Claim("azp", "11111111-1111-1111-1111-111111111111"));
                }
            }
        }

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
