using Azure;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;

namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Extension methods for <see cref="WebApplicationBuilder"/> to configure Azure App Configuration and Key Vault.
/// </summary>
public static class AppConfigurationExtensions {
    internal const string AppConfigurationEnabledKey = "AppConfig:Enabled";

    public static WebApplicationBuilder AddAzureAppConfigurationWithKeyVault(this WebApplicationBuilder builder) {
        var appConfigConnectionString = builder.Configuration["AppConfig:ConnectionString"];
        var appConfigEndpoint = builder.Configuration["AppConfig:Endpoint"];

        if (!string.IsNullOrEmpty(appConfigConnectionString) || !string.IsNullOrEmpty(appConfigEndpoint)) {
            var useRemoteInDevelopment = IsEnabled(builder.Configuration["AppConfig:UseRemoteInDevelopment"]);
            if (builder.Environment.IsDevelopment() && !useRemoteInDevelopment) {
                Console.WriteLine(
                    "Azure App Configuration skipped in Development. " +
                    "Set AppConfig:UseRemoteInDevelopment=true and sign in with Azure CLI to load remote configuration locally.");
                builder.Configuration[AppConfigurationEnabledKey] = bool.FalseString;
            }
            else {
                // Use ManagedIdentityCredential in non-development environments
                TokenCredential credential = builder.Environment.IsDevelopment()
                    ? new AzureCliCredential()
                    : new ManagedIdentityCredential(new ManagedIdentityCredentialOptions());

                // Pre-warm the managed identity token before loading App Config in non-dev envs
                if (string.IsNullOrEmpty(appConfigConnectionString) &&
                    !string.IsNullOrEmpty(appConfigEndpoint) &&
                    !builder.Environment.IsDevelopment()) {
                    PreWarmManagedIdentityTokenAsync(credential).GetAwaiter().GetResult();
                    EnsureTcpConnectivityAsync(appConfigEndpoint).GetAwaiter().GetResult();
                }

                try {
                    builder.Configuration.AddAzureAppConfiguration(options => {
                        if (!string.IsNullOrEmpty(appConfigConnectionString)) {
                            options.Connect(appConfigConnectionString);
                        }
                        else {
                            options.Connect(new Uri(appConfigEndpoint!), credential);
                        }

                        options.Select(KeyFilter.Any)
                               .Select(KeyFilter.Any, "api")
                               .Select(KeyFilter.Any, builder.Environment.EnvironmentName)
                               .ConfigureKeyVault(kv => kv.SetCredential(credential))
                               .ConfigureRefresh(refreshOptions => {
                                   refreshOptions.Register("Settings:Sentinel", refreshAll: true)
                                                 .SetRefreshInterval(TimeSpan.FromSeconds(30));
                               });
                    });

                    builder.Services.AddAzureAppConfiguration();
                    builder.Configuration[AppConfigurationEnabledKey] = bool.TrueString;
                }
                catch (Exception ex) when (CanSkipAzureAppConfigurationFailure(builder, appConfigConnectionString, ex)) {
                    Console.WriteLine(
                        "Azure App Configuration skipped in Development because remote configuration could not be loaded. " +
                        $"Reason: {ex.GetType().Name}. Configure an Azure developer login or unset AppConfig:Endpoint to use local settings only.");
                    builder.Configuration[AppConfigurationEnabledKey] = bool.FalseString;
                }
            }
        }

        // Validate and derive configuration values (when config is built to IConfigurationRoot)
        if (builder.Configuration is IConfigurationRoot configRoot) {
            configRoot.WithDerivedAzureAdValues();
            configRoot.WithValidatedAzureAIEndpoints();
            configRoot.WithDerivedBlobStorageValues();
        }

        return builder;
    }

    private static bool IsEnabled(string? value) =>
        bool.TryParse(value, out var enabled) && enabled;

    private static bool CanSkipAzureAppConfigurationFailure(WebApplicationBuilder builder, string? appConfigConnectionString, Exception exception) {
        if (!builder.Environment.IsDevelopment() || !string.IsNullOrEmpty(appConfigConnectionString)) {
            return false;
        }

        return exception is CredentialUnavailableException or AuthenticationFailedException or RequestFailedException or TimeoutException ||
               exception is AggregateException aggregateException &&
               aggregateException.InnerExceptions.Any(inner => CanSkipAzureAppConfigurationFailure(builder, appConfigConnectionString, inner));
    }

    private static async Task PreWarmManagedIdentityTokenAsync(TokenCredential credential) {
        var tokenCtx = new TokenRequestContext(["https://azconfig.io/.default"]);
        for (var attempt = 1; attempt <= 10; attempt++) {
            try {
                await credential.GetTokenAsync(tokenCtx, CancellationToken.None);
                Console.WriteLine($"Managed identity token acquired on attempt {attempt}.");
                break;
            }
            catch (Exception ex) when (attempt < 10) {
                Console.WriteLine($"IMDS not ready (attempt {attempt}/10): {ex.Message}. Retrying in 3s...");
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }

    private static async Task EnsureTcpConnectivityAsync(string appConfigEndpoint) {
        var appConfigUri = new Uri(appConfigEndpoint);
        var appConfigHost = appConfigUri.Host;
        for (var attempt = 1; attempt <= 15; attempt++) {
            try {
                using var tcp = new System.Net.Sockets.TcpClient();
                var connectTask = tcp.ConnectAsync(appConfigHost, 443);
                if (await Task.WhenAny(connectTask, Task.Delay(5000)) == connectTask) {
                    await connectTask;
                    Console.WriteLine($"App Config TCP connectivity confirmed on attempt {attempt}.");
                    break;
                }
                if (attempt < 15) {
                    Console.WriteLine($"App Config TCP timed out (attempt {attempt}/15). Retrying in 2s...");
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
            catch (Exception ex) when (attempt < 15) {
                Console.WriteLine($"App Config TCP failed (attempt {attempt}/15): {ex.Message}. Retrying in 2s...");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    /// <summary>
    /// Validates and populates Azure AD configuration defaults.
    /// Derives JWT issuer URLs and audience values from TenantId and ClientId.
    /// </summary>
    public static IConfigurationRoot WithDerivedAzureAdValues(this IConfigurationRoot configuration) {
        var tenantId = configuration["AzureAd:TenantId"];
        var clientId = configuration["AzureAd:ClientId"];
        var configuredAudience = configuration["Authentication:Audience"] ?? configuration["AzureAd:Audience"];

        if (string.IsNullOrWhiteSpace(tenantId)) {
            throw new InvalidOperationException("AzureAd Tenant ID is not configured");
        }

        var effectiveAudience = string.IsNullOrWhiteSpace(configuredAudience)
            ? clientId
            : configuredAudience;

        var derivedConfig = new Dictionary<string, string?>
        {
            { "AzureAd:Audience", effectiveAudience },
            { "Authentication:Audience", effectiveAudience },
            { "Jwt:ValidAudience", effectiveAudience },
            { "Jwt:ValidIssuer", $"https://login.microsoftonline.com/{tenantId}/v2.0" },
            { "Jwt:IssuerSigningKeyUrl", $"https://login.microsoftonline.com/{tenantId}/discovery/v2.0/keys" },
            { "Authentication:Issuers:Workforce", $"https://login.microsoftonline.com/{tenantId}/v2.0" }
        };

        // Set values on all providers that support it (especially MemoryConfigurationProvider)
        foreach (var kvp in derivedConfig.Where(x => x.Value != null)) {
            foreach (var provider in configuration.Providers) {
                provider.Set(kvp.Key, kvp.Value!);
            }
        }

        configuration.Reload();
        return configuration;
    }

    /// <summary>
    /// Validates Azure AI service endpoints.
    /// </summary>
    public static IConfigurationRoot WithValidatedAzureAIEndpoints(this IConfigurationRoot configuration) {
        var searchEndpoint = configuration["AzureAI:SearchServiceEndpoint"];
        var documentIntelligenceEndpoint = configuration["AzureAI:DocumentIntelligenceEndpoint"];
        var foundryEndpoint = configuration["AzureAI:FoundryEndpoint"];

        ValidateEndpointIfProvided("Search", searchEndpoint, "AzureAI:SearchServiceEndpoint");
        ValidateEndpointIfProvided("Document Intelligence", documentIntelligenceEndpoint, "AzureAI:DocumentIntelligenceEndpoint");
        ValidateEndpointIfProvided("Foundry", foundryEndpoint, "AzureAI:FoundryEndpoint");

        return configuration;
    }

    /// <summary>
    /// Derives Blob Storage account endpoint from the configured Data Protection blob URI
    /// when the explicit BlobStorage:AccountEndpoint setting is not present.
    /// </summary>
    public static IConfigurationRoot WithDerivedBlobStorageValues(this IConfigurationRoot configuration) {
        var configuredAccountEndpoint = configuration["BlobStorage:AccountEndpoint"];
        if (!string.IsNullOrWhiteSpace(configuredAccountEndpoint)) {
            return configuration;
        }

        var dataProtectionBlobUri = configuration["DataProtection:BlobUri"];
        if (string.IsNullOrWhiteSpace(dataProtectionBlobUri)) {
            return configuration;
        }

        if (!Uri.TryCreate(dataProtectionBlobUri, UriKind.Absolute, out var blobUri)) {
            return configuration;
        }

        var derivedAccountEndpoint = blobUri.GetLeftPart(UriPartial.Authority);
        foreach (var provider in configuration.Providers) {
            provider.Set("BlobStorage:AccountEndpoint", derivedAccountEndpoint);
        }

        configuration.Reload();
        return configuration;
    }

    private static void ValidateEndpointIfProvided(string serviceName, string? endpoint, string configKey) {
        if (string.IsNullOrWhiteSpace(endpoint)) {
            return;
        }

        if (!endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Azure {serviceName} endpoint MUST use HTTPS protocol.");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https") {
            throw new InvalidOperationException($"Azure {serviceName} endpoint is not a valid HTTPS URL.");
        }

        if (endpoint.Contains("your-", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Contains("example", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Contains("placeholder", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Azure {serviceName} endpoint appears to be a placeholder.");
        }
    }
}
