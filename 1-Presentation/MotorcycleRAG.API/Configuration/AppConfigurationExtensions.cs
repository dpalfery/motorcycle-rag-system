using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;

namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Extension methods for <see cref="WebApplicationBuilder"/> to configure Azure App Configuration and Key Vault.
/// </summary>
internal static class AppConfigurationExtensions
{
    public static WebApplicationBuilder AddAzureAppConfigurationWithKeyVault(this WebApplicationBuilder builder)
    {
        var appConfigEndpoint = builder.Configuration["AppConfig:Endpoint"];
        
        if (!string.IsNullOrEmpty(appConfigEndpoint))
        {
            // Use ManagedIdentityCredential in non-development environments
            TokenCredential credential = builder.Environment.IsDevelopment()
                ? new DefaultAzureCredential()
                : new ManagedIdentityCredential();

            // Pre-warm the managed identity token before loading App Config in non-dev envs
            if (!builder.Environment.IsDevelopment())
            {
                PreWarmManagedIdentityTokenAsync(credential).GetAwaiter().GetResult();
                EnsureTcpConnectivityAsync(appConfigEndpoint).GetAwaiter().GetResult();
            }

            builder.Configuration.AddAzureAppConfiguration(options =>
            {
                options.Connect(new Uri(appConfigEndpoint), credential)
                       .Select(KeyFilter.Any)
                       .Select(KeyFilter.Any, "api")
                       .Select(KeyFilter.Any, builder.Environment.EnvironmentName)
                       .ConfigureKeyVault(kv => kv.SetCredential(credential))
                       .ConfigureRefresh(refreshOptions =>
                       {
                           refreshOptions.Register("Settings:Sentinel", refreshAll: true)
                                         .SetRefreshInterval(TimeSpan.FromSeconds(30));
                       });
            });

            builder.Services.AddAzureAppConfiguration();
        }

        // Validate and derive configuration values
        builder.Configuration.WithDerivedAzureAdValues();
        builder.Configuration.WithValidatedAzureAIEndpoints();

        return builder;
    }

    private static async Task PreWarmManagedIdentityTokenAsync(TokenCredential credential)
    {
        var tokenCtx = new TokenRequestContext(["https://azconfig.io/.default"]);
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                await credential.GetTokenAsync(tokenCtx, CancellationToken.None);
                Console.WriteLine($"Managed identity token acquired on attempt {attempt}.");
                break;
            }
            catch (Exception ex) when (attempt < 10)
            {
                Console.WriteLine($"IMDS not ready (attempt {attempt}/10): {ex.Message}. Retrying in 3s...");
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }

    private static async Task EnsureTcpConnectivityAsync(string appConfigEndpoint)
    {
        var appConfigUri = new Uri(appConfigEndpoint);
        var appConfigHost = appConfigUri.Host;
        for (var attempt = 1; attempt <= 15; attempt++)
        {
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                var connectTask = tcp.ConnectAsync(appConfigHost, 443);
                if (await Task.WhenAny(connectTask, Task.Delay(5000)) == connectTask)
                {
                    await connectTask;
                    Console.WriteLine($"App Config TCP connectivity confirmed on attempt {attempt}.");
                    break;
                }
                if (attempt < 15)
                {
                    Console.WriteLine($"App Config TCP timed out (attempt {attempt}/15). Retrying in 2s...");
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
            catch (Exception ex) when (attempt < 15)
            {
                Console.WriteLine($"App Config TCP failed (attempt {attempt}/15): {ex.Message}. Retrying in 2s...");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    /// <summary>
    /// Validates and populates Azure AD configuration defaults.
    /// Derives JWT issuer URLs and audience values from TenantId and ClientId.
    /// </summary>
    internal static IConfiguration WithDerivedAzureAdValues(this IConfiguration configuration)
    {
        var tenantId = configuration["AzureAd:TenantId"];
        var clientId = configuration["AzureAd:ClientId"];

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException("Azure AD Tenant ID is not configured.");
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Azure AD Client ID is not configured.");
        }

        var derivedConfig = new Dictionary<string, string?>
        {
            { "AzureAd:Audience", clientId },
            { "Authentication:Audience", clientId },
            { "Jwt:ValidAudience", clientId },
            { "Jwt:ValidIssuer", $"https://login.microsoftonline.com/{tenantId}/v2.0" },
            { "Jwt:IssuerSigningKeyUrl", $"https://login.microsoftonline.com/{tenantId}/discovery/v2.0/keys" },
            { "Authentication:Issuers:Workforce", $"https://login.microsoftonline.com/{tenantId}/v2.0" }
        };

        // We use a separate ConfigurationBuilder for the derived values to merge them back
        var derivedSource = new ConfigurationBuilder()
            .AddInMemoryCollection(derivedConfig)
            .Build();

        foreach (var kvp in derivedSource.AsEnumerable().Where(x => x.Value != null))
        {
            // Note: Since IConfiguration doesn't directly support adding memory collections once built,
            // we rely on the specific configuration implementation if possible, or assume it's a 
            // ConfigurationBuilder session. In the context of WebApplicationBuilder.Configuration,
            // it's a ConfigurationManager which supports IConfigurationBuilder.
            if (configuration is IConfigurationBuilder cb)
            {
                cb.AddInMemoryCollection(new[] { kvp });
            }
            else
            {
                // Fallback for when we only have the raw IConfiguration (like in unit tests)
                configuration[kvp.Key] = kvp.Value;
            }
        }

        return configuration;
    }

    /// <summary>
    /// Validates Azure AI service endpoints.
    /// </summary>
    internal static IConfiguration WithValidatedAzureAIEndpoints(this IConfiguration configuration)
    {
        var searchEndpoint = configuration["AzureAI:SearchServiceEndpoint"];
        var documentIntelligenceEndpoint = configuration["AzureAI:DocumentIntelligenceEndpoint"];
        var foundryEndpoint = configuration["AzureAI:FoundryEndpoint"];

        ValidateEndpointIfProvided("Search", searchEndpoint, "AzureAI:SearchServiceEndpoint");
        ValidateEndpointIfProvided("Document Intelligence", documentIntelligenceEndpoint, "AzureAI:DocumentIntelligenceEndpoint");
        ValidateEndpointIfProvided("Foundry", foundryEndpoint, "AzureAI:FoundryEndpoint");

        return configuration;
    }

    private static void ValidateEndpointIfProvided(string serviceName, string? endpoint, string configKey)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        if (!endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Azure {serviceName} endpoint MUST use HTTPS protocol.");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https")
        {
            throw new InvalidOperationException($"Azure {serviceName} endpoint is not a valid HTTPS URL.");
        }

        if (endpoint.Contains("your-", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Contains("example", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Azure {serviceName} endpoint appears to be a placeholder.");
        }
    }
}
