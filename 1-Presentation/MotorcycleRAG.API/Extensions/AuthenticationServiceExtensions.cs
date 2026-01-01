using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace MotorcycleRAG.API.Extensions;

/// <summary>
/// Cache for OpenID signing keys with background refresh.
/// Fetches signing keys asynchronously to avoid blocking the request pipeline.
/// </summary>
internal class SigningKeyCache
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SigningKeyCache> _logger;
    private readonly ConcurrentDictionary<string, (List<SecurityKey> Keys, DateTime Expiry)> _keyCache;
    private readonly TimeSpan _cacheTtl = TimeSpan.FromHours(1);  // Cache keys for 1 hour
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);  // Prevent concurrent refreshes

    public SigningKeyCache(HttpClient httpClient, ILogger<SigningKeyCache> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _keyCache = new ConcurrentDictionary<string, (List<SecurityKey>, DateTime)>();
    }

    /// <summary>
    /// Pre-warms the cache with signing keys from the specified issuers.
    /// Should be called during application startup to avoid blocking requests.
    /// </summary>
    public async Task PreWarmCacheAsync(string workforceIssuer, string? externalIdIssuer = null)
    {
        _logger.LogInformation("Pre-warming signing key cache for issuers");

        // Warm up the workforce issuer
        if (!string.IsNullOrEmpty(workforceIssuer))
        {
            await RefreshKeysAsync(workforceIssuer);
        }

        // Warm up the external ID issuer if configured
        if (!string.IsNullOrEmpty(externalIdIssuer))
        {
            await RefreshKeysAsync(externalIdIssuer);
        }

        _logger.LogInformation("Signing key cache pre-warming completed");
    }

    /// <summary>
    /// Gets signing keys for an issuer from the cache.
    /// Returns cached keys without blocking. Cache must be pre-warmed at startup.
    /// </summary>
    public IEnumerable<SecurityKey> GetSigningKeys(string issuer)
    {
        if (string.IsNullOrEmpty(issuer))
            return [];

        // Check if we have cached keys and they're still valid
        if (_keyCache.TryGetValue(issuer, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            _logger.LogDebug("Returning cached signing keys for issuer {Issuer}", issuer);
            return cached.Keys;
        }

        // Cache miss - either cache wasn't pre-warmed or keys expired
        // Return empty to reject token, triggering fallback handling
        // A warning is logged to alert operators that cache pre-warming may have failed
        _logger.LogWarning(
            "Signing keys not available in cache for issuer {Issuer}. " +
            "Cache may not have been pre-warmed at startup. Token will be rejected.",
            issuer);
        return [];
    }

    /// <summary>
    /// Asynchronously refreshes signing keys for an issuer from OpenID metadata.
    /// Safe to call from background operations, but not from request pipeline.
    /// </summary>
    private async Task RefreshKeysAsync(string issuer)
    {
        // Use semaphore to prevent multiple concurrent refreshes for same issuer
        await _refreshSemaphore.WaitAsync();
        try
        {
            // Double-check cache after acquiring semaphore
            if (_keyCache.TryGetValue(issuer, out var cached) && cached.Expiry > DateTime.UtcNow)
                return;

            var keys = await FetchSigningKeysAsync(issuer);
            if (keys.Any())
            {
                var expiry = DateTime.UtcNow.Add(_cacheTtl);
                _keyCache[issuer] = (keys, expiry);
                _logger.LogInformation(
                    "Cached {KeyCount} signing keys for issuer {Issuer} (expires {Expiry})",
                    keys.Count, issuer, expiry);
            }
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    /// <summary>
    /// Fetches signing keys from the issuer's OpenID discovery endpoint.
    /// </summary>
    private async Task<List<SecurityKey>> FetchSigningKeysAsync(string issuer)
    {
        try
        {
            var metadataAddress = $"{issuer.TrimEnd('/')}/.well-known/openid-configuration";

            _logger.LogDebug("Fetching OpenID metadata from {MetadataAddress}", metadataAddress);
            var metadataResponse = await _httpClient.GetAsync(metadataAddress);
            metadataResponse.EnsureSuccessStatusCode();

            var metadataJson = await metadataResponse.Content.ReadAsStringAsync();
            var metadataDoc = JsonDocument.Parse(metadataJson);

            if (!metadataDoc.RootElement.TryGetProperty("jwks_uri", out var jwksUriElement))
            {
                _logger.LogWarning("No jwks_uri found in OpenID metadata for issuer {Issuer}", issuer);
                return [];
            }

            var jwksUri = jwksUriElement.GetString();
            if (string.IsNullOrEmpty(jwksUri))
            {
                _logger.LogWarning("jwks_uri is empty in OpenID metadata for issuer {Issuer}", issuer);
                return [];
            }

            _logger.LogDebug("Fetching signing keys from {JwksUri}", jwksUri);
            var keysResponse = await _httpClient.GetAsync(jwksUri);
            keysResponse.EnsureSuccessStatusCode();

            var keysJson = await keysResponse.Content.ReadAsStringAsync();
            var keysDoc = JsonDocument.Parse(keysJson);

            var signingKeys = new List<SecurityKey>();

            if (keysDoc.RootElement.TryGetProperty("keys", out var keysArray))
            {
                foreach (var keyElement in keysArray.EnumerateArray())
                {
                    try
                    {
                        var keyJson = keyElement.GetRawText();
                        var jsonWebKey = JsonSerializer.Deserialize<JsonWebKey>(keyJson);
                        if (jsonWebKey != null)
                        {
                            signingKeys.Add(jsonWebKey);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse signing key from issuer {Issuer}", issuer);
                    }
                }
            }

            _logger.LogDebug("Successfully fetched {KeyCount} signing keys from issuer {Issuer}",
                signingKeys.Count, issuer);

            return signingKeys;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching signing keys from issuer {Issuer}", issuer);
            return [];
        }
    }
}

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

        // Register a singleton cache for OpenID keys to avoid blocking on network I/O during request processing
        authenticationBuilder.Services.AddSingleton<SigningKeyCache>();

        // Register HttpClient as singleton to avoid socket exhaustion
        authenticationBuilder.Services.AddHttpClient<SigningKeyCache>()
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);  // Timeout for metadata fetches
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

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
                // Keys will be retrieved from cache below
            };

            // Implement dual-issuer signing key resolution using cached keys
            // The cache is pre-warmed on startup and refreshed in background
            options.TokenValidationParameters.IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
            {
                var jsonToken = securityToken as JsonWebToken;
                var issuer = jsonToken?.Issuer;

                if (string.IsNullOrEmpty(issuer))
                {
                    logger.LogWarning("Token missing issuer claim");
                    return [];
                }

                // Get the key cache from the service provider
                var httpContextAccessor = authenticationBuilder.Services
                    .BuildServiceProvider()
                    .GetRequiredService<IHttpContextAccessor>();

                var keyCache = httpContextAccessor?.HttpContext?.RequestServices
                    ?.GetService(typeof(SigningKeyCache)) as SigningKeyCache;

                if (keyCache == null)
                {
                    logger.LogError("Unable to resolve SigningKeyCache from request services");
                    return [];
                }

                try
                {
                    // Retrieve cached signing keys for this issuer (never blocks - uses pre-cached keys)
                    var keys = keyCache.GetSigningKeys(issuer).ToList();

                    // If kid is provided, validate it
                    if (!string.IsNullOrEmpty(kid))
                    {
                        var matchedKey = keys.FirstOrDefault(k => k.KeyId == kid);
                        if (matchedKey == null)
                        {
                            logger.LogWarning(
                                "Token kid ({Kid}) not found in signing keys for issuer {Issuer}. Available kids: {AvailableKids}",
                                kid,
                                issuer,
                                string.Join(", ", keys.Select(k => k.KeyId ?? "unknown")));
                            return [];  // Reject token with unmatched kid
                        }
                        return [matchedKey];
                    }

                    logger.LogDebug("Resolved {KeyCount} signing keys for issuer {Issuer} (kid: {Kid})",
                        keys.Count, issuer, kid ?? "not specified");
                    return keys;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error retrieving signing keys for issuer {Issuer}", issuer);
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
