
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRag.WebUI.BFF.Configuration.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MotorcycleRag.WebUI.BFF.Controllers;

#pragma warning disable CA1812 // AuthController is instantiated by ASP.NET Core routing via reflection
#pragma warning disable S3059 // Public methods required for ASP.NET Core controller routing
[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase {
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IHttpClientFactory httpClientFactory, ILogger<AuthController> logger) {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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
    public async Task<ActionResult> GetUser() {
        if (User.Identity != null && User.Identity.IsAuthenticated) {
            var accessToken = await HttpContext.GetTokenAsync("access_token").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(accessToken)) {
                _logger.LogWarning("Authenticated BFF session is missing an access token for approval-state lookup");
                return Ok(new {
                    authenticated = false,
                    sessionAuthenticated = true,
                    accessApproved = false,
                    approvalStatus = "Unknown",
                    approvalMessage = "Your sign-in session is established, but access approval could not be verified."
                });
            }

            try {
                using var client = _httpClientFactory.CreateClient(AuthenticationServiceConfiguration.ApprovalStatusHttpClientName);
                if (client.BaseAddress == null) {
                    _logger.LogWarning("Approval-status HTTP client has no API base address configured");
                    return Ok(new {
                        authenticated = false,
                        sessionAuthenticated = true,
                        accessApproved = false,
                        approvalStatus = "Unknown",
                        approvalMessage = "Your sign-in session is established, but access approval could not be verified."
                    });
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, "api/me");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    HttpContext.RequestAborted).ConfigureAwait(false);

                var responseBody = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) {
                    return Ok(new {
                        authenticated = true,
                        sessionAuthenticated = true,
                        accessApproved = true,
                        approvalStatus = "Approved",
                        user = User.Identity.Name ?? "User",
                        managedUserId = TryGetJsonString(responseBody, "id"),
                        email = TryGetJsonString(responseBody, "email"),
                        planName = TryGetJsonString(responseBody, "planName")
                    });
                }

                var approvalMessage = TryGetJsonString(responseBody, "error")
                    ?? TryGetJsonString(responseBody, "title")
                    ?? "Access has not been approved for this account.";

                if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Unauthorized) {
                    return Ok(new {
                        authenticated = false,
                        sessionAuthenticated = true,
                        accessApproved = false,
                        approvalStatus = response.StatusCode == HttpStatusCode.Forbidden ? "ApprovalRequired" : "ApiUnauthorized",
                        approvalMessage,
                        user = User.Identity.Name ?? "User"
                    });
                }

                _logger.LogWarning(
                    "Unexpected approval-state lookup result from API: {StatusCode}",
                    (int)response.StatusCode);
                return Ok(new {
                    authenticated = false,
                    sessionAuthenticated = true,
                    accessApproved = false,
                    approvalStatus = "Unknown",
                    approvalMessage = "Your sign-in session is established, but access approval could not be verified.",
                    user = User.Identity.Name ?? "User"
                });
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to resolve approval-aware auth state from the API");
                return Ok(new {
                    authenticated = false,
                    sessionAuthenticated = true,
                    accessApproved = false,
                    approvalStatus = "Unknown",
                    approvalMessage = "Your sign-in session is established, but access approval could not be verified.",
                    user = User.Identity.Name ?? "User"
                });
            }
        }

        // Return 200 (not 401) - this is a polling endpoint, not a protected resource
        return Ok(new {
            authenticated = false,
            sessionAuthenticated = false,
            accessApproved = false,
            approvalStatus = "SignedOut"
        });
    }

    private static string? TryGetJsonString(string responseBody, string propertyName) {
        if (string.IsNullOrWhiteSpace(responseBody)) {
            return null;
        }

        try {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty(propertyName, out var property)) {
                return null;
            }

            return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
        }
        catch (JsonException) {
            return null;
        }
    }
}
