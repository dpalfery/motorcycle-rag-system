using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;

namespace MotorcycleRag.WebUI.BFF.Configuration;

/// <summary>
/// Extension methods for <see cref="WebApplicationBuilder"/> to configure Azure App Configuration for the BFF.
/// </summary>
internal static class AppConfigurationExtensions {
    public static WebApplicationBuilder AddBffAzureAppConfiguration(this WebApplicationBuilder builder) {
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
            });

            builder.Services.AddAzureAppConfiguration();
        }

        return builder;
    }

    private static async Task PreWarmManagedIdentityTokenAsync(TokenCredential credential) {
        var tokenCtx = new TokenRequestContext(["https://azconfig.io/.default"]);
        for (var attempt = 1; attempt <= 10; attempt++) {
            try {
                await credential.GetTokenAsync(tokenCtx, CancellationToken.None);
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
                    break;
                }
                if (attempt < 15) {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
            catch (Exception) when (attempt < 15) {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }
}
