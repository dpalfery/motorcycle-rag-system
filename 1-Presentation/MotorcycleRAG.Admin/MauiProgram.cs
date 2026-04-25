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

        // Configure rolling file logger for persistent diagnostics
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MotorcycleRAGAdmin", "logs");
        var fileLoggerOptions = new Services.Logging.FileLoggerOptions {
            LogDirectory = logDirectory,
            MinimumLevel = LogLevel.Trace
        };
        builder.Logging.AddProvider(new Services.Logging.FileLoggerProvider(fileLoggerOptions));

#if DEBUG
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
#else
        builder.Logging.SetMinimumLevel(LogLevel.Information);
#endif
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);  // suppresses handler expiry noise
        builder.Logging.AddFilter("Polly", LogLevel.None); // retry/circuit-breaker telemetry is noise; failures surface via ViewModel error handlers

        // ========== Core Services (Singletons) ==========

        // File Logger Options - provides log directory path to SettingsViewModel
        builder.Services.AddSingleton(fileLoggerOptions);

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

                client.Timeout = TimeSpan.FromSeconds(120);
            })
            .AddStandardResilienceHandler()
            .Configure(options => {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30); // Container App cold-starts can take 10-30s
                options.Retry.MaxRetryAttempts = 1; // 0 is invalid; 1 = one retry after initial failure
                options.CircuitBreaker.MinimumThroughput = 5;
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(90); // must be >= 2x AttemptTimeout (30s)
                options.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(2);
            });

        // ========== Local Processing Services ==========

        builder.Services.AddSingleton<PdfChunker>(_ => new PdfChunker());
        builder.Services.AddSingleton<CsvChunker>(_ => new CsvChunker());

        // ========== Pages ==========
        // Shell ContentTemplate caches the page instance for the Shell lifetime,
        // so Shell pages MUST be Singleton to avoid BindingContext becoming stale.
        builder.Services.AddSingleton<DashboardPage>();
        builder.Services.AddTransient<LandingPage>(); // Not in Shell — transient is correct
        builder.Services.AddSingleton<UploadPage>();
        builder.Services.AddSingleton<JobsPage>();
        builder.Services.AddSingleton<UserManagementPage>();
        builder.Services.AddSingleton<WebSourcesPage>();
        builder.Services.AddSingleton<ToolsPage>();
        builder.Services.AddSingleton<SettingsPage>();

        // ========== ViewModels ==========
        // Shell pages are Singleton so their ViewModels must also be Singleton to match
        // the page lifetime (avoids the VM being GC'd while the page is still alive).
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddTransient<LandingViewModel>(); // Matches LandingPage lifetime
        builder.Services.AddSingleton<IngestionViewModel>();
        builder.Services.AddSingleton<JobsViewModel>();
        builder.Services.AddSingleton<UserManagementViewModel>();
        builder.Services.AddSingleton<WebSourcesViewModel>();
        builder.Services.AddSingleton<ToolsViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

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
