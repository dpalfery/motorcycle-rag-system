using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;

namespace MotorcycleRag.WebUI.BFF.Configuration;

/// <summary>
/// Extension methods for <see cref="WebApplicationBuilder"/> to configure Azure App Configuration for the BFF.
/// </summary>
internal static class AppConfigurationExtensions {
    public static WebApplicationBuilder AddBffAzureAppConfiguration(
        this WebApplicationBuilder builder,
        bool optional = false,
        bool skipRemoteConfigurationLoad = false) {
        var appConfigConnectionString = builder.Configuration["AppConfig:ConnectionString"];
        var appConfigEndpoint = builder.Configuration["AppConfig:Endpoint"];

        if (builder.Environment.IsDevelopment()) {
            if (!string.IsNullOrEmpty(appConfigConnectionString) || !string.IsNullOrEmpty(appConfigEndpoint)) {
                Console.WriteLine("Azure App Configuration skipped in Development. Using the local .NET configuration sources only.");
            }

            return builder;
        }

        if (!string.IsNullOrEmpty(appConfigConnectionString) || !string.IsNullOrEmpty(appConfigEndpoint)) {
            TokenCredential credential = new ManagedIdentityCredential(new ManagedIdentityCredentialOptions());

            if (string.IsNullOrEmpty(appConfigConnectionString) && !string.IsNullOrEmpty(appConfigEndpoint)) {
                PreWarmManagedIdentityTokenAsync(credential).GetAwaiter().GetResult();
                EnsureTcpConnectivityAsync(appConfigEndpoint).GetAwaiter().GetResult();
            }

            // Unit tests set skipRemoteConfigurationLoad to avoid Azure's multi-minute startup timeout.
            if (!skipRemoteConfigurationLoad) {
                builder.Configuration.AddAzureAppConfiguration(options => {
                    if (!string.IsNullOrEmpty(appConfigConnectionString)) {
                        options.Connect(appConfigConnectionString);
                    }
                    else {
                        options.Connect(new Uri(appConfigEndpoint!), credential);
                    }

                    options.Select(KeyFilter.Any)
                           .Select(KeyFilter.Any, "bff")
                           .Select(KeyFilter.Any, builder.Environment.EnvironmentName)
                           .ConfigureKeyVault(kv => kv.SetCredential(credential))
                           .ConfigureRefresh(refreshOptions => {
                               refreshOptions.Register("Settings:Sentinel", refreshAll: true)
                                             .SetRefreshInterval(TimeSpan.FromSeconds(30));
                           });
                }, optional);
            }

            builder.Services.AddAzureAppConfiguration();
        }

        return builder;
    }

    private static async Task PreWarmManagedIdentityTokenAsync(
        TokenCredential credential,
        int maxAttempts = 10,
        int retryDelayMs = 3000) {
        var tokenCtx = new TokenRequestContext(["https://azconfig.io/.default"]);
        for (var attempt = 1; attempt <= maxAttempts; attempt++) {
            try {
                await credential.GetTokenAsync(tokenCtx, CancellationToken.None);
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts) {
                Console.WriteLine($"IMDS not ready (attempt {attempt}/{maxAttempts}): {ex.Message}. Retrying in {retryDelayMs}ms...");
                await Task.Delay(TimeSpan.FromMilliseconds(retryDelayMs));
            }
        }
    }

    private static async Task EnsureTcpConnectivityAsync(
        string appConfigEndpoint,
        int maxAttempts = 15,
        int connectTimeoutMs = 5000,
        int retryDelayMs = 2000,
        int port = 443) {
        var appConfigUri = new Uri(appConfigEndpoint);
        var appConfigHost = appConfigUri.Host;
        for (var attempt = 1; attempt <= maxAttempts; attempt++) {
            try {
                using var tcp = new System.Net.Sockets.TcpClient();
                using var connectCts = new CancellationTokenSource(connectTimeoutMs);
                await tcp.ConnectAsync(appConfigHost, port, connectCts.Token);
                break;
            }
            catch (OperationCanceledException) {
                if (attempt < maxAttempts) {
                    await Task.Delay(TimeSpan.FromMilliseconds(retryDelayMs));
                }
            }
            catch (Exception) when (attempt < maxAttempts) {
                await Task.Delay(TimeSpan.FromMilliseconds(retryDelayMs));
            }
        }
    }
}
