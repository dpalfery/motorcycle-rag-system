using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Linq;

namespace MotorcycleRag.WebUI.BFF.Configuration.Services;

/// <summary>
/// Configuration for authentication services in the BFF.
/// </summary>
internal static class AuthenticationServiceConfiguration {
    internal const string ApprovalStatusHttpClientName = "bff-approval-status";

    public static IServiceCollection AddBffAuthentication(
        this IServiceCollection services,
        IConfiguration configuration) {
        var apiClusterAddress = GetApiClusterAddress(configuration);

        services.AddHttpClient(ApprovalStatusHttpClientName, client => {
            if (apiClusterAddress != null) {
                client.BaseAddress = apiClusterAddress;
            }
        });

        services.AddAuthentication(options => {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(options => {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Name = "MotorcycleRAG"; // No __Host- prefix: ACA terminates TLS at edge; container receives plain HTTP
            options.Cookie.Path = "/";
            options.Cookie.IsEssential = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(1);
            options.SlidingExpiration = true;
            options.Cookie.MaxAge = TimeSpan.FromHours(1);
            options.Cookie.Domain = null; // Prevent subdomain attacks
        })
        .AddOpenIdConnect(options => {
            var authConfig = configuration.GetSection("AzureAd");

            // CIAM (Entra External ID) discovery document at /v2.0/.well-known/openid-configuration
            options.Authority = $"{authConfig["Instance"]}{authConfig["TenantId"]}/v2.0";
            options.ClientId = authConfig["ClientId"];
            options.ClientSecret = configuration["AzureAd:ClientSecret"]
                ?? throw new InvalidOperationException(
                    "BFF Client Secret is not configured. " +
                    "Provide AzureAd:ClientSecret through Azure App Configuration with a Key Vault reference.");

            options.ResponseType = OpenIdConnectResponseType.Code;
            options.SaveTokens = true;
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.Scope.Add("offline_access"); // Request refresh token

            // API scopes
            options.Scope.Add("api://motorcyclerag-api/read");
            options.Scope.Add("api://motorcyclerag-api/chat");

            // Redirect hardening
            options.ProtocolValidator.RequireNonce = true;

            // PKCE protection
            options.UsePkce = true;
            options.ResponseMode = OpenIdConnectResponseMode.Query;

            // Token validation
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidateLifetime = true;
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            options.TokenValidationParameters.RequireExpirationTime = true;
            options.TokenValidationParameters.RequireSignedTokens = true;

            // ACA terminates TLS at the edge; the container receives plain http:// requests.
            options.Events = new OpenIdConnectEvents {
                OnRedirectToIdentityProvider = context => {
                    context.ProtocolMessage.RedirectUri = context.ProtocolMessage.RedirectUri
                        .Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);
                    return Task.CompletedTask;
                },
                OnRemoteFailure = context => {
                    context.HandleResponse();
                    context.Response.Redirect("/signin?error=auth_failed");
                    return Task.CompletedTask;
                }
            };

            // OIDC correlation + nonce cookies must survive the cross-site Entra redirect callback.
            options.NonceCookie.SameSite = SameSiteMode.None;
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.CorrelationCookie.SameSite = SameSiteMode.None;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        return services;
    }

    private static Uri? GetApiClusterAddress(IConfiguration configuration) {
        var destinations = configuration.GetSection("ReverseProxy:Clusters:api-cluster:Destinations").GetChildren();
        var address = destinations
            .Select(destination => destination["Address"])
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) {
            return null;
        }

        return uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri($"{uri.AbsoluteUri}/", UriKind.Absolute);
    }
}
