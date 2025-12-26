
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MotorcycleRag.WebUI.BFF.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    [HttpGet("login")]
    public ActionResult Login(string returnUrl = "/")
    {
        return Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpPost("logout")]
    public async Task<ActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
        return Ok(new { message = "Logged out" });
    }

    [HttpGet("me")]
    public ActionResult GetUser()
    {
        if (User.Identity != null && User.Identity.IsAuthenticated)
        {
            var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();
            return Ok(new 
            { 
                authenticated = true, 
                user = User.Identity.Name ?? "User",
                claims
            });
        }
        return Unauthorized(new { authenticated = false });
    }
}
