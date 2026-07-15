using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;

namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Extension methods for <see cref="WebApplicationBuilder"/> to configure Azure App Configuration and Key Vault.
/// </summary>
public static class AppConfigurationExtensions {
    internal const string AppConfigurationEnabledKey = "AppConfig:Enabled";
    internal static Action<IConfigurationBuilder, string?, string?, TokenCredential, string> RegisterAzureAppConfiguration =
        RegisterAzureAppConfigurationCore;
    internal static Func<TokenCredential, Task> PreWarmManagedIdentityTokenAsyncForStartup =
        PreWarmManagedIdentityTokenAsync;
    internal static Func<string, Task> EnsureTcpConnectivityAsyncForStartup =
        EnsureTcpConnectivityAsync;
    internal static Func<string, int, Task> ConnectToAppConfigurationAsyncForStartup =
        ConnectToAppConfigurationAsync;

    public static WebApplicationBuilder AddAzureAppConfigurationWithKeyVault(this WebApplicationBuilder builder) {
        var appConfigConnectionString = builder.Configuration["AppConfig:ConnectionString"];
        var appConfigEndpoint = builder.Configuration["AppConfig:Endpoint"];

        if (builder.Environment.IsDevelopment()) {
            if (!string.IsNullOrEmpty(appConfigConnectionString) || !string.IsNullOrEmpty(appConfigEndpoint)) {
                Console.WriteLine("Azure App Configuration skipped in Development. Using the local .NET configuration sources only.");
            }

            builder.Configuration[AppConfigurationEnabledKey] = bool.FalseString;
        }
        else if (!string.IsNullOrEmpty(appConfigConnectionString) || !string.IsNullOrEmpty(appConfigEndpoint)) {
            TokenCredential credential = new ManagedIdentityCredential(new ManagedIdentityCredentialOptions());

            // Pre-warm the managed identity token before loading App Config in non-dev envs
            if (string.IsNullOrEmpty(appConfigConnectionString) && !string.IsNullOrEmpty(appConfigEndpoint)) {
                PreWarmManagedIdentityTokenAsyncForStartup(credential).GetAwaiter().GetResult();
                EnsureTcpConnectivityAsyncForStartup(appConfigEndpoint).GetAwaiter().GetResult();
            }

            RegisterAzureAppConfiguration(
                builder.Configuration,
                appConfigConnectionString,
                appConfigEndpoint,
                credential,
                builder.Environment.EnvironmentName);

            builder.Services.AddAzureAppConfiguration();
            builder.Configuration[AppConfigurationEnabledKey] = bool.TrueString;
        }

        // WebApplicationBuilder.Configuration is always an IConfigurationRoot.
        builder.Configuration.WithDerivedAzureAdValues();
        builder.Configuration.WithValidatedAzureAIEndpoints();
        builder.Configuration.WithDerivedBlobStorageValues();

        return builder;
    }

    private static Task PreWarmManagedIdentityTokenAsync(TokenCredential credential) =>
        PreWarmManagedIdentityTokenAsync(credential, maxAttempts: 10, retryDelay: TimeSpan.FromSeconds(3));

    internal static async Task PreWarmManagedIdentityTokenAsync(
        TokenCredential credential,
        int maxAttempts,
        TimeSpan retryDelay) {
        var tokenCtx = new TokenRequestContext(["https://azconfig.io/.default"]);
        for (var attempt = 1; attempt <= maxAttempts; attempt++) {
            try {
                await credential.GetTokenAsync(tokenCtx, CancellationToken.None);
                Console.WriteLine($"Managed identity token acquired on attempt {attempt}.");
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts) {
                Console.WriteLine($"IMDS not ready (attempt {attempt}/{maxAttempts}): {ex.Message}. Retrying in {retryDelay.TotalSeconds}s...");
                await Task.Delay(retryDelay);
            }
        }
    }

    private static Task EnsureTcpConnectivityAsync(string appConfigEndpoint) =>
        EnsureTcpConnectivityAsync(
            appConfigEndpoint,
            ConnectToAppConfigurationAsyncForStartup,
            TimeProvider.System,
            maxAttempts: 15,
            connectTimeout: TimeSpan.FromSeconds(5),
            retryDelay: TimeSpan.FromSeconds(2));

    internal static async Task EnsureTcpConnectivityAsync(
        string appConfigEndpoint,
        Func<string, int, Task> connectAsync,
        TimeProvider? timeProvider,
        int maxAttempts,
        TimeSpan? connectTimeout,
        TimeSpan? retryDelay) {
        var provider = timeProvider ?? TimeProvider.System;
        var appConfigUri = new Uri(appConfigEndpoint);
        var appConfigHost = appConfigUri.Host;
        var effectiveConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5);
        var effectiveRetryDelay = retryDelay ?? TimeSpan.FromSeconds(2);

        for (var attempt = 1; attempt <= maxAttempts; attempt++) {
            try {
                var connectTask = connectAsync(appConfigHost, 443);
                if (await Task.WhenAny(connectTask, Task.Delay(effectiveConnectTimeout, provider)) == connectTask) {
                    await connectTask;
                    Console.WriteLine($"App Config TCP connectivity confirmed on attempt {attempt}.");
                    return;
                }

                Console.WriteLine($"App Config TCP timed out (attempt {attempt}/{maxAttempts}).");
            }
            catch (Exception ex) when (attempt < maxAttempts) {
                Console.WriteLine($"App Config TCP failed (attempt {attempt}/{maxAttempts}): {ex.Message}.");
                Console.WriteLine("Retrying in 2s...");
                await Task.Delay(effectiveRetryDelay, provider);
            }
        }
    }

    private static async Task ConnectToAppConfigurationAsync(string host, int port) {
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(host, port);
    }

    private static void RegisterAzureAppConfigurationCore(
        IConfigurationBuilder configuration,
        string? connectionString,
        string? endpoint,
        TokenCredential credential,
        string environmentName) {
        configuration.AddAzureAppConfiguration(options =>
            ConfigureAzureAppConfigurationOptions(options, connectionString, endpoint, credential, environmentName));
    }

    internal static void ConfigureAzureAppConfigurationOptions(
        AzureAppConfigurationOptions options,
        string? connectionString,
        string? endpoint,
        TokenCredential credential,
        string environmentName) {
        if (!string.IsNullOrEmpty(connectionString)) {
            options.Connect(connectionString);
        }
        else {
            options.Connect(new Uri(endpoint!), credential);
        }

        options.Select(KeyFilter.Any)
               .Select(KeyFilter.Any, "api")
               .Select(KeyFilter.Any, environmentName)
               .ConfigureKeyVault(kv => kv.SetCredential(credential))
               .ConfigureRefresh(refreshOptions => {
                   refreshOptions.Register("Settings:Sentinel", refreshAll: true)
                                 .SetRefreshInterval(TimeSpan.FromSeconds(30));
               });
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

        ValidateEndpointIfProvided("Search", searchEndpoint);
        ValidateEndpointIfProvided("Document Intelligence", documentIntelligenceEndpoint);
        ValidateEndpointIfProvided("Foundry", foundryEndpoint);

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

    private static void ValidateEndpointIfProvided(string serviceName, string? endpoint) {
        if (string.IsNullOrWhiteSpace(endpoint)) {
            return;
        }

        if (!endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Azure {serviceName} endpoint MUST use HTTPS protocol.");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _)) {
            throw new InvalidOperationException($"Azure {serviceName} endpoint is not a valid HTTPS URL.");
        }

        if (endpoint.Contains("your-", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Contains("example", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Contains("placeholder", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Azure {serviceName} endpoint appears to be a placeholder.");
        }
    }
}
