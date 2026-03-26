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

    internal SigningKeyCache(HttpClient httpClient, ILogger<SigningKeyCache> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _keyCache = new ConcurrentDictionary<string, (List<SecurityKey>, DateTime)>();
    }

    /// <summary>
    /// Pre-warms the cache with signing keys from the specified issuers.
    /// Should be called during application startup to avoid blocking requests.
    /// </summary>
    internal async Task PreWarmCacheAsync(string workforceIssuer, string? externalIdIssuer = null)
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
    internal IEnumerable<SecurityKey> GetSigningKeys(string issuer)
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
            var metadataResponse = await _httpClient.GetAsync(new Uri(metadataAddress));
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
            var keysResponse = await _httpClient.GetAsync(new Uri(jwksUri));
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
internal static class AuthenticationServiceExtensions
{
    private const string NamedApplicationIdUri = "api://motorcyclerag-api";

    /// <summary>
    /// Configures JWT bearer options for dual-issuer authentication.
    /// </summary>
    private static void ConfigureJwtBearerOptions(
        JwtBearerOptions options,
        SigningKeyCache keyCache,
        string workforceIssuer,
        string? externalIdIssuer,
        IReadOnlyCollection<string> validAudiences)
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
            ValidAudiences = validAudiences,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(300), // 5 minutes for clock skew
            ValidateIssuerSigningKey = true,
        };

        // Implement dual-issuer signing key resolution using cached keys
        options.TokenValidationParameters.IssuerSigningKeyResolver = (_, securityToken, kid, _) =>
        {
            var jsonToken = securityToken as JsonWebToken;
            var issuer = jsonToken?.Issuer;

            if (string.IsNullOrEmpty(issuer))
                return [];

            try
            {
                var keys = keyCache.GetSigningKeys(issuer).ToList();

                if (!string.IsNullOrEmpty(kid))
                {
                    var matchedKey = keys.FirstOrDefault(k => k.KeyId == kid);
                    return matchedKey == null ? [] : [matchedKey];
                }

                return keys;
            }
            catch
            {
                return [];
            }
        };

        // Configure JWT bearer events for logging and diagnostics
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var localLogger = context.HttpContext.RequestServices.GetService<ILogger<JwtBearerEvents>>();
                localLogger?.LogWarning("Authentication failed: {Exception}", context.Exception.Message);
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var localLogger = context.HttpContext.RequestServices.GetService<ILogger<JwtBearerEvents>>();
                if (context.Principal?.Claims != null)
                {
                    var tokenIssuer = context.Principal.FindFirst("iss")?.Value ?? "unknown";
                    var tokenSubject = context.Principal.FindFirst("sub")?.Value ?? "unknown";
                    localLogger?.LogDebug(
                        "Token validated. Issuer: {Issuer}, Subject: {Subject}",
                        tokenIssuer,
                        tokenSubject);
                }
                return Task.CompletedTask;
            },
        };
    }

    private static IReadOnlyCollection<string> BuildValidAudiences(
        IConfiguration configuration,
        string audience)
    {
        var validAudiences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddAudienceAlias(validAudiences, audience);
        AddAudienceAlias(validAudiences, configuration["AzureAd:ClientId"]);

        // MotorcycleRAG currently exposes both the GUID-based and named Application ID URIs.
        // Keep trusting the named URI so older admin tokens minted for api://motorcyclerag-api
        // continue to validate while the client configuration is being cleaned up.
        AddAudienceAlias(validAudiences, NamedApplicationIdUri);

        foreach (var configuredAudience in configuration.GetSection("Authentication:AdditionalAudiences").GetChildren())
        {
            AddAudienceAlias(validAudiences, configuredAudience.Value);
        }

        return validAudiences.ToArray();
    }

    private static void AddAudienceAlias(ISet<string> audiences, string? audience)
    {
        if (string.IsNullOrWhiteSpace(audience))
        {
            return;
        }

        var trimmedAudience = audience.Trim();
        audiences.Add(trimmedAudience);

        if (trimmedAudience.StartsWith("api://", StringComparison.OrdinalIgnoreCase))
        {
            var resourceId = trimmedAudience["api://".Length..];
            if (Guid.TryParse(resourceId, out _))
            {
                audiences.Add(resourceId);
            }

            return;
        }

        if (Guid.TryParse(trimmedAudience, out _))
        {
            audiences.Add($"api://{trimmedAudience}");
        }
    }

    /// <summary>
    /// Adds dual-issuer JWT bearer authentication supporting both Entra ID and Entra External ID/B2C.
    /// </summary>
    /// <param name="authenticationBuilder">Authentication builder</param>
    /// <param name="configuration">Application configuration</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <returns>Authentication builder for chaining</returns>
    internal static AuthenticationBuilder AddDualIssuerJwtBearer(
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

        var validAudiences = BuildValidAudiences(configuration, audience);

        logger.LogInformation(
            "Configuring dual-issuer JWT authentication. Workforce: {WorkforceIssuer}, ExternalId: {ExternalIdIssuer}, ValidAudiences: {ValidAudiences}",
            workforceIssuer,
            externalIdIssuer ?? "<not configured>",
            validAudiences);

        // Register HttpClient for the signing key cache
        authenticationBuilder.Services.AddHttpClient("SigningKeyCache")
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);  // Timeout for metadata fetches
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

        // Register a singleton cache for OpenID keys to avoid blocking on network I/O during request processing
        authenticationBuilder.Services.AddSingleton(sp =>
            new SigningKeyCache(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("SigningKeyCache"),
                sp.GetRequiredService<ILogger<SigningKeyCache>>()));


        // Configure JWT bearer options with dependency injection support
        authenticationBuilder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SigningKeyCache>((options, keyCache) =>
            {
                ConfigureJwtBearerOptions(options, keyCache, workforceIssuer, externalIdIssuer, validAudiences);
            });

        // Register JWT bearer handler with configured options
        return authenticationBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options => { });
    }
}
