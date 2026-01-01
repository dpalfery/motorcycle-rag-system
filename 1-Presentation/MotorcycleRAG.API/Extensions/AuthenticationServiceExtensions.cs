using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace MotorcycleRAG.API.Extensions;

/// <summary>
/// Extension methods for configuring authentication services.
/// Implements dual-issuer JWT validation to support both Entra ID (workforce) and Entra External ID/B2C (customers).
/// </summary>
public static class AuthenticationServiceExtensions
{
    /// <summary>
    /// Adds dual-issuer JWT bearer authentication supporting both Entra ID and Entra External ID/B2C.
    /// </summary>
    /// <param name="authenticationBuilder">Authentication builder</param>
    /// <param name="configuration">Application configuration</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <returns>Authentication builder for chaining</returns>
    public static AuthenticationBuilder AddDualIssuerJwtBearer(
        this AuthenticationBuilder authenticationBuilder,
        IConfiguration configuration,
        ILogger logger)
    {
        var workforceIssuer = configuration["Authentication:Issuers:Workforce"];
        var externalIdIssuer = configuration["Authentication:Issuers:ExternalId"];
        var audience = configuration["Authentication:Audience"];

        if (string.IsNullOrWhiteSpace(workforceIssuer))
        {
            throw new InvalidOperationException(
                "Workforce issuer is not configured in Authentication:Issuers:Workforce");
        }

        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException(
                "Audience is not configured in Authentication:Audience");
        }

        logger.LogInformation(
            "Configuring dual-issuer JWT authentication. Workforce: {WorkforceIssuer}, ExternalId: {ExternalIdIssuer}",
            workforceIssuer,
            externalIdIssuer ?? "<not configured>");

        return authenticationBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            // Build the list of valid issuers
            var validIssuers = new List<string> { workforceIssuer };
            if (!string.IsNullOrWhiteSpace(externalIdIssuer))
            {
                validIssuers.Add(externalIdIssuer);
            }

            // Configure token validation
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = validIssuers,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(300), // 5 minutes for clock skew
                ValidateIssuerSigningKey = true,
                // Keys will be retrieved from the Microsoft identity provider
            };

            // Configure JWT security token handler
            options.SecurityTokenValidators.Clear();
            options.SecurityTokenValidators.Add(new JwtSecurityTokenHandler
            {
                MapInboundClaims = false // Preserve original claim types (don't map to Windows claims)
            });

            // Challenge/forbidden handling
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    logger.LogWarning(
                        "Authentication failed: {Exception}",
                        context.Exception.Message);
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    if (context.Principal?.Claims != null)
                    {
                        var tokenIssuer = context.Principal.FindFirst("iss")?.Value ?? "unknown";
                        var tokenSubject = context.Principal.FindFirst("sub")?.Value ?? "unknown";
                        logger.LogDebug(
                            "Token validated. Issuer: {Issuer}, Subject: {Subject}",
                            tokenIssuer,
                            tokenSubject);
                    }
                    return Task.CompletedTask;
                },
            };

            // Configure authority for metadata retrieval
            options.Authority = workforceIssuer;
            options.MetadataAddress = $"{workforceIssuer}/.well-known/openid-configuration";
        });
    }
}
