using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http.Resilience;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Pages;
using MotorcycleRAG.Admin.ViewModels;
using MotorcycleRAG.Admin.Processing;

namespace MotorcycleRAG.Admin;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "S1200:Split this class into smaller and more specialized ones",
    Justification = "Composition root naturally has many dependencies")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "S3059:Types should not have members with visibility set higher than the type's visibility",
    Justification = "Allowed: composition root has internal type with internal members by design for MAUI startup")]
internal static class MauiProgram {
    internal static MauiApp CreateMauiApp() {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Configure logging for all build configurations
        // Using Debug provider which works across all MAUI platforms
        builder.Logging.AddDebug();
#if DEBUG
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#else
        builder.Logging.SetMinimumLevel(LogLevel.Information);
#endif
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Logging.AddFilter("Polly", LogLevel.Error);

        // ========== Core Services (Singletons) ==========

        // Navigation Service - enables testable ViewModels
        builder.Services.AddSingleton<INavigationService, NavigationService>();

        // Settings Service - wraps Preferences and SecureStorage for testability
        builder.Services.AddSingleton<ISettingsService, SettingsService>();

        // Configuration State Service - tracks app configuration state
        builder.Services.AddSingleton<IConfigurationStateService, ConfigurationStateService>();
        builder.Services.AddSingleton<IAppFlowCoordinator, AppFlowCoordinator>();
        builder.Services.AddSingleton<ILocalProcessorService, LocalProcessorService>();
        builder.Services.AddHttpClient(ApiWarmupService.HttpClientName, client => {
            client.Timeout = TimeSpan.FromSeconds(95);
        });
        builder.Services.AddSingleton<IApiWarmupService, ApiWarmupService>();

        // Authentication Service - reacts to saved settings rather than locking config at startup
        builder.Services.AddSingleton<IAdminAuthService, ConfigurableAdminAuthService>();

        // ========== HTTP Client with Resilience Policies ==========

        // Configure HttpClient with resilience handlers (retry, circuit breaker, timeout)
        // Using Microsoft.Extensions.Http.Resilience for production-grade policies
        builder.Services
            .AddHttpClient<ApiClient>((sp, client) => {
                var configService = sp.GetRequiredService<IConfigurationStateService>();
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("MauiProgram.HttpClient");

                // API settings come only from values persisted through the app's Settings page.
                Uri? apiUri = configService.ApiBaseUrl;

                // If we have a URL, validate and configure it
                if (apiUri != null) {
                    // Validate HTTPS in production (non-localhost URLs must use HTTPS)
                    var isLocalhost = apiUri.IsLoopback;

                    if (!isLocalhost && !apiUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) {
                        logger.LogWarning(
                            "Saved API base URL must use HTTPS for non-localhost URLs. API calls will fail until corrected in Settings.");
                    }
                    else {
                        client.BaseAddress = apiUri;
                        logger.LogInformation("API base URL configured: {Url}", apiUri.Host);
                    }
                }
                else {
                    logger.LogWarning("API base URL not configured. Open Settings to configure it.");
                }

                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddStandardResilienceHandler()
            .Configure(options => {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                options.Retry.MaxRetryAttempts = 0;
                options.CircuitBreaker.MinimumThroughput = 5;
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(2);
            });

        // ========== Local Processing Services ==========

        builder.Services.AddSingleton<PdfChunker>(_ => new PdfChunker());
        builder.Services.AddSingleton<CsvChunker>(_ => new CsvChunker());

        // ========== Pages (Transient - Fresh instance per navigation) ==========

        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<LandingPage>();
        builder.Services.AddTransient<UploadPage>();
        builder.Services.AddTransient<JobsPage>();
        builder.Services.AddTransient<WebSourcesPage>();
        builder.Services.AddTransient<ToolsPage>();
        builder.Services.AddTransient<SettingsPage>();

        // ========== ViewModels (Transient - Fresh instance per navigation) ==========

        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<LandingViewModel>();
        builder.Services.AddTransient<IngestionViewModel>();
        builder.Services.AddTransient<JobsViewModel>();
        builder.Services.AddTransient<WebSourcesViewModel>();
        builder.Services.AddTransient<ToolsViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // Register AppShell (Singleton - single instance for app lifetime)
        builder.Services.AddSingleton<AppShell>(sp =>
            new AppShell(
                sp.GetRequiredService<IAdminAuthService>(),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<IConfigurationStateService>(),
                sp));

        // Register App as transient for MAUI to resolve
        builder.Services.AddTransient<App>();

        return builder.Build();
    }
}
