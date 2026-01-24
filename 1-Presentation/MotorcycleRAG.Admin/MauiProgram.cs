using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http.Resilience;
using Polly;
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

        // ========== Core Services (Singletons) ==========

        // Navigation Service - enables testable ViewModels
        builder.Services.AddSingleton<INavigationService, NavigationService>();

        // Settings Service - wraps Preferences and SecureStorage for testability
        builder.Services.AddSingleton<ISettingsService, SettingsService>();

        // Configuration State Service - tracks app configuration state
        builder.Services.AddSingleton<IConfigurationStateService, ConfigurationStateService>();

        // Authentication Service - uses ConfigurationStateService to determine if configured
        builder.Services.AddSingleton<IAdminAuthService>(sp => {
            var configService = sp.GetRequiredService<IConfigurationStateService>();
            var logger = sp.GetRequiredService<ILogger<AdminAuthService>>();
            var demoLogger = sp.GetRequiredService<ILogger<DemoAdminAuthService>>();

            // Check if auth is configured via UI settings
            if (configService.IsAuthConfigured) {
                logger.LogInformation("Using configured authentication settings");
                return new AdminAuthService(
                    clientId: configService.AuthClientId!,
                    authority: configService.AuthAuthority!,
                    scopes: new[] { configService.AuthScope! },
                    logger: logger
                );
            }

            // Fall back to environment variables for backward compatibility
            var envClientId = Environment.GetEnvironmentVariable("MCR_ADMIN_CLIENT_ID");
            var envAuthority = Environment.GetEnvironmentVariable("MCR_ADMIN_AUTHORITY");
            var envScope = Environment.GetEnvironmentVariable("MCR_ADMIN_API_SCOPE");

            if (!string.IsNullOrEmpty(envClientId) &&
                !string.IsNullOrEmpty(envAuthority) &&
                !string.IsNullOrEmpty(envScope)) {
                logger.LogInformation("Using environment variable authentication settings");
                return new AdminAuthService(
                    clientId: envClientId,
                    authority: envAuthority,
                    scopes: new[] { envScope },
                    logger: logger
                );
            }

            // No configuration available - use demo service
            return new DemoAdminAuthService(demoLogger);
        });

        // ========== HTTP Client with Resilience Policies ==========

        // Configure HttpClient with resilience handlers (retry, circuit breaker, timeout)
        // Using Microsoft.Extensions.Http.Resilience for production-grade policies
        builder.Services
            .AddHttpClient<ApiClient>((sp, client) => {
                var configService = sp.GetRequiredService<IConfigurationStateService>();
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("MauiProgram.HttpClient");

                // Try UI-configured API URL first
                Uri? apiUri = configService.ApiBaseUrl;

                // Fall back to environment variable
                if (apiUri == null) {
                    var envUrl = Environment.GetEnvironmentVariable("MCR_ADMIN_API_BASE_URL");
                    if (!string.IsNullOrEmpty(envUrl) && Uri.TryCreate(envUrl, UriKind.Absolute, out var parsedUri)) {
                        apiUri = parsedUri;
                    }
                }

                // If we have a URL, validate and configure it
                if (apiUri != null) {
                    // Validate HTTPS in production (non-localhost URLs must use HTTPS)
                    var isLocalhost = apiUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                                      apiUri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);

                    if (!isLocalhost && !apiUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) {
                        logger.LogWarning(
                            "MCR_ADMIN_API_BASE_URL must use HTTPS for non-localhost URLs. API calls will fail until configured correctly.");
                    }
                    else {
                        client.BaseAddress = apiUri;
                        logger.LogInformation("API base URL configured: {Url}", apiUri.Host);
                    }
                }
                else {
                    logger.LogWarning("API base URL not configured. Go to Settings to configure.");
                }

                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddStandardResilienceHandler(); // Includes retry + circuit breaker policies

        // ========== Local Processing Services ==========

        builder.Services.AddSingleton<PdfChunker>();
        builder.Services.AddSingleton<CsvChunker>();

        // Optional: ONNX embedding service (requires model file in Resources/Raw/)
        // Only register if model is available - IngestionViewModel will use server-side
        // embedding processing as fallback if this service is not registered
        // Note: We can't log here since DI container isn't built yet.
        // The warning will be logged by OnnxEmbeddingServiceFactory when it tries to load the model.
        try {
            // Register factory method so DI container manages disposal
            builder.Services.AddSingleton<OnnxEmbeddingService>(_ =>
                OnnxEmbeddingServiceFactory.CreateFromAppResources());
        }
        catch (Exception) {
            // Model not available - don't register the service
            // IngestionViewModel will handle missing service gracefully
            // We can't log here since logger isn't available yet
        }

        // ========== Pages (Transient - Fresh instance per navigation) ==========

        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<UploadPage>();
        builder.Services.AddTransient<JobsPage>();
        builder.Services.AddTransient<WebSourcesPage>();
        builder.Services.AddTransient<ToolsPage>();
        builder.Services.AddTransient<SettingsPage>();

        // ========== ViewModels (Transient - Fresh instance per navigation) ==========

        builder.Services.AddTransient<DashboardViewModel>();
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
