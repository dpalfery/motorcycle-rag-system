using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;
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

        // External ID is optional, but log a warning if not configured to alert operators
        if (string.IsNullOrWhiteSpace(externalIdIssuer))
        {
            logger.LogWarning(
                "External ID issuer is not configured in Authentication:Issuers:ExternalId. " +
                "Only Workforce (Entra ID) authentication will be supported. " +
                "Configure this if you need to support B2C/External ID customers.");
        }
        else
        {
            logger.LogInformation("External ID issuer configured for dual-issuer JWT authentication");
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
                // Keys will be retrieved dynamically via IssuerSigningKeyResolver below
            };

            // Implement dual-issuer signing key resolution
            options.TokenValidationParameters.IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
            {
                var jsonToken = securityToken as JsonWebToken;
                var issuer = jsonToken?.Issuer;

                if (string.IsNullOrEmpty(issuer))
                {
                    logger.LogWarning("Token missing issuer claim");
                    return [];
                }

                try
                {
                    // Fetch signing keys from the appropriate issuer's OpenID metadata
                    var metadataAddress = $"{issuer.TrimEnd('/')}/.well-known/openid-configuration";
                    var handler = new HttpClientHandler();
                    var httpClient = new HttpClient(handler);

                    var metadata = httpClient.GetAsync(metadataAddress).Result;
                    metadata.EnsureSuccessStatusCode();

                    var jsonMetadata = metadata.Content.ReadAsStringAsync().Result;
                    var metadataDoc = System.Text.Json.JsonDocument.Parse(jsonMetadata);

                    var keysUrl = metadataDoc.RootElement.GetProperty("jwks_uri").GetString();
                    if (string.IsNullOrEmpty(keysUrl))
                    {
                        logger.LogWarning("No jwks_uri found in metadata for issuer {Issuer}", issuer);
                        return [];
                    }

                    var keysResponse = httpClient.GetAsync(keysUrl).Result;
                    keysResponse.EnsureSuccessStatusCode();

                    var keysJson = keysResponse.Content.ReadAsStringAsync().Result;
                    var keysDoc = System.Text.Json.JsonDocument.Parse(keysJson);

                    var signingKeys = new List<SecurityKey>();
                    var keys = keysDoc.RootElement.GetProperty("keys");

                    foreach (var key in keys.EnumerateArray())
                    {
                        var keyJson = key.GetRawText();
                        var jsonWebKey = System.Text.Json.JsonSerializer.Deserialize<JsonWebKey>(keyJson);
                        if (jsonWebKey != null)
                        {
                            signingKeys.Add(jsonWebKey);
                        }
                    }

                    logger.LogDebug("Resolved {KeyCount} signing keys for issuer {Issuer}", signingKeys.Count, issuer);
                    return signingKeys;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error resolving signing keys for issuer {Issuer}", issuer);
                    return [];
                }
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

            // Do NOT set Authority or MetadataAddress - use IssuerSigningKeyResolver instead
            // This allows the resolver to fetch keys from the correct issuer based on the token's 'iss' claim
        });
    }
}
