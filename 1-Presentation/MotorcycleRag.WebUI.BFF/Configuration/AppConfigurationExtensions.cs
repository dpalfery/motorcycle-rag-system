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
        bool skipRemoteConfigurationLoad = false) =>
        AddBffAzureAppConfiguration(
            builder,
            optional,
            skipRemoteConfigurationLoad,
            static () => new ManagedIdentityCredential(new ManagedIdentityCredentialOptions()),
            static credential => PreWarmManagedIdentityTokenAsync(credential),
            static endpoint => EnsureTcpConnectivityAsync(endpoint),
            RegisterAzureAppConfiguration);

    internal static WebApplicationBuilder AddBffAzureAppConfiguration(
        this WebApplicationBuilder builder,
        bool optional,
        bool skipRemoteConfigurationLoad,
        Func<TokenCredential> createCredential,
        Func<TokenCredential, Task> preWarmManagedIdentityTokenAsync,
        Func<string, Task> ensureTcpConnectivityAsync,
        Action<IConfigurationBuilder, string?, string?, TokenCredential, string, bool> registerAzureAppConfiguration,
        Action<AzureAppConfigurationRegistrationIntent>? observeRegistrationIntent = null) {
        var appConfigConnectionString = builder.Configuration["AppConfig:ConnectionString"];
        var appConfigEndpoint = builder.Configuration["AppConfig:Endpoint"];

        if (builder.Environment.IsDevelopment()) {
            if (!string.IsNullOrEmpty(appConfigConnectionString) || !string.IsNullOrEmpty(appConfigEndpoint)) {
                Console.WriteLine("Azure App Configuration skipped in Development. Using the local .NET configuration sources only.");
            }

            return builder;
        }

        if (!string.IsNullOrEmpty(appConfigConnectionString) || !string.IsNullOrEmpty(appConfigEndpoint)) {
            var credential = createCredential();

            if (!skipRemoteConfigurationLoad && string.IsNullOrEmpty(appConfigConnectionString) && !string.IsNullOrEmpty(appConfigEndpoint)) {
                preWarmManagedIdentityTokenAsync(credential).GetAwaiter().GetResult();
                ensureTcpConnectivityAsync(appConfigEndpoint).GetAwaiter().GetResult();
            }

            if (!skipRemoteConfigurationLoad) {
                observeRegistrationIntent?.Invoke(new AzureAppConfigurationRegistrationIntent(
                    appConfigConnectionString,
                    appConfigEndpoint,
                    optional,
                    "bff",
                    builder.Environment.EnvironmentName,
                    credential,
                    "Settings:Sentinel",
                    true,
                    TimeSpan.FromSeconds(30)));
                registerAzureAppConfiguration(
                    builder.Configuration,
                    appConfigConnectionString,
                    appConfigEndpoint,
                    credential,
                    builder.Environment.EnvironmentName,
                    optional);
            }

            builder.Services.AddAzureAppConfiguration();
        }

        return builder;
    }

    internal sealed record AzureAppConfigurationRegistrationIntent(
        string? ConnectionString,
        string? Endpoint,
        bool Optional,
        string ComponentLabel,
        string EnvironmentLabel,
        TokenCredential KeyVaultCredential,
        string SentinelKey,
        bool RefreshAll,
        TimeSpan RefreshInterval) {
        public bool UsesConnectionString => !string.IsNullOrEmpty(ConnectionString);
    }

    internal static async Task PreWarmManagedIdentityTokenAsync(
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

    internal static Task EnsureTcpConnectivityAsync(
        string appConfigEndpoint,
        int maxAttempts = 15,
        int connectTimeoutMs = 5000,
        int retryDelayMs = 2000,
        int port = 443) =>
        EnsureTcpConnectivityWithRetriesAsync(
            appConfigEndpoint,
            ConnectToAppConfigurationAsync,
            TimeProvider.System,
            maxAttempts,
            TimeSpan.FromMilliseconds(connectTimeoutMs),
            TimeSpan.FromMilliseconds(retryDelayMs),
            port);

    internal static async Task EnsureTcpConnectivityWithRetriesAsync(
        string appConfigEndpoint,
        Func<string, int, CancellationToken, Task> connectAsync,
        TimeProvider timeProvider,
        int maxAttempts,
        TimeSpan connectTimeout,
        TimeSpan retryDelay,
        int port = 443) {
        var appConfigUri = new Uri(appConfigEndpoint);
        var appConfigHost = appConfigUri.Host;
        for (var attempt = 1; attempt <= maxAttempts; attempt++) {
            try {
                using var connectCts = new CancellationTokenSource(connectTimeout);
                await connectAsync(appConfigHost, port, connectCts.Token);
                break;
            }
            catch (OperationCanceledException) {
                if (attempt < maxAttempts) {
                    await Task.Delay(retryDelay, timeProvider);
                }
            }
            catch (Exception) when (attempt < maxAttempts) {
                await Task.Delay(retryDelay, timeProvider);
            }
        }
    }

    internal static async Task ConnectToAppConfigurationAsync(string host, int port, CancellationToken cancellationToken) {
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(host, port, cancellationToken);
    }

    internal static void RegisterAzureAppConfiguration(
        IConfigurationBuilder configuration,
        string? appConfigConnectionString,
        string? appConfigEndpoint,
        TokenCredential credential,
        string environmentName,
        bool optional) {
        configuration.AddAzureAppConfiguration(
            options => ConfigureAzureAppConfigurationOptions(
                options,
                appConfigConnectionString,
                appConfigEndpoint,
                credential,
                environmentName),
            optional);
    }

    internal static void ConfigureAzureAppConfigurationOptions(
        AzureAppConfigurationOptions options,
        string? appConfigConnectionString,
        string? appConfigEndpoint,
        TokenCredential credential,
        string environmentName) {
        if (!string.IsNullOrEmpty(appConfigConnectionString)) {
            options.Connect(appConfigConnectionString);
        }
        else {
            options.Connect(new Uri(appConfigEndpoint!), credential);
        }

        options.Select(KeyFilter.Any)
               .Select(KeyFilter.Any, "bff")
               .Select(KeyFilter.Any, environmentName)
               .ConfigureKeyVault(kv => kv.SetCredential(credential))
               .ConfigureRefresh(refreshOptions => {
                   refreshOptions.Register("Settings:Sentinel", refreshAll: true)
                                 .SetRefreshInterval(TimeSpan.FromSeconds(30));
               });
    }
}
