
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace MotorcycleRag.WebUI.BFF.Controllers;

#pragma warning disable CA1812 // AuthController is instantiated by ASP.NET Core routing via reflection
#pragma warning disable S3059 // Public methods required for ASP.NET Core controller routing
[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase {
#pragma warning restore S3059
#pragma warning restore CA1812
    [HttpGet("login")]
    public ActionResult Login([FromQuery] Uri? returnUrl = null) {
        var target = returnUrl?.ToString() ?? "/";
        return Challenge(new AuthenticationProperties { RedirectUri = target }, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpPost("logout")]
    public async Task<ActionResult> Logout() {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme).ConfigureAwait(false);
        return Ok(new { message = "Logged out" });
    }

    [HttpGet("me")]
    public ActionResult GetUser() {
        if (User.Identity != null && User.Identity.IsAuthenticated) {
            // Return only essential user information; do NOT expose raw claims
            // Raw claims may contain sensitive scopes, roles, OID, TID, and other metadata
            return Ok(new {
                authenticated = true,
                user = User.Identity.Name ?? "User"
            });
        }
        // Return 200 (not 401) - this is a polling endpoint, not a protected resource
        return Ok(new { authenticated = false });
    }
}
