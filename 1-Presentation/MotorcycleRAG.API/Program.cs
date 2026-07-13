using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.API.Configuration.Services;
using MotorcycleRAG.API.Extensions;
using MotorcycleRAG.API.Services;
using MotorcycleRAG.Application.Extensions;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Contracts.Interfaces;
using TelemetryOptions = MotorcycleRAG.Core.Options.TelemetryOptions;

namespace MotorcycleRAG.API;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Program class must be public for functional tests.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1052:Static holder types should be Static or NotInheritable",
    Justification = "Program class must be valid generic type argument for " +
    "WebApplicationFactory.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "Composition root inherently has high coupling")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Minor",
    "S1118:Utility classes should not have public constructors",
    Justification = "Program must be instantiable for WebApplicationFactory-based tests.")]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class Program {
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static async Task Main(string[] args) {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder);

        // 7. Build and Configure Pipeline
        var app = builder.Build();

        app.UseMotorcycleRagMiddleware();

        await app.PreWarmJwtSigningKeysAsync();
        await app.RunAsync();
    }

    /// <summary>
    /// Adds the API's production services to the supplied host builder.
    /// Kept separate from host startup so composition can be validated without starting
    /// hosted services or performing startup-time remote discovery.
    /// </summary>
    internal static void ConfigureServices(WebApplicationBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);

        var configuration = builder.Configuration;

        // 1. Core Configuration & Logging
        builder.Logging.AddStructuredLogging(builder.Environment);
        builder.AddAzureAppConfigurationWithKeyVault();

        if (builder.Environment.IsDevelopment()) {
            // Ensure local secrets and env vars override App Config in development
            builder.Configuration.AddUserSecrets<Program>(optional: true);
            builder.Configuration.AddEnvironmentVariables();
        }

        // 2. Telemetry & Monitoring
        var telemetryOptions = builder.Configuration.GetSection("ApplicationInsights").Get<TelemetryOptions>() ?? new TelemetryOptions();
        builder.Services.AddMotorcycleRagTelemetry(telemetryOptions);

        // 3. MVC & Core Services
        builder.Services.AddControllers();
        builder.Services.AddAntiforgery();

        var ingestionRequestLimitBytes = configuration
            .GetSection("Ingestion")
            .Get<IngestionOptions>()?.MaxInputBytes ?? new IngestionOptions().MaxInputBytes;

        // Keep server-level limits aligned with the ingestion upload policy.
        builder.WebHost.ConfigureKestrel(options => {
            options.Limits.MaxRequestBodySize = ingestionRequestLimitBytes;
        });

        builder.Services.Configure<FormOptions>(options => {
            options.MultipartBodyLengthLimit = ingestionRequestLimitBytes;
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
        builder.Services.ConfigureJsonSerialization(builder.Environment.IsDevelopment());
        builder.Services.Configure<AppOptions>(configuration);
        builder.Services.Configure<OnboardingOptions>(configuration.GetSection("Onboarding"));

        // 4. API Documentation & Connectivity
        builder.Services.AddApiDocumentation();
        builder.Services.AddRestrictedCors(configuration);

        // Disable built-in HostFilter in favor of custom HostHeaderValidationMiddleware
        builder.Services.PostConfigure<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>(options => {
            options.AllowedHosts = ["*"];
        });

        // 5. Domain & Infrastructure Services (Existing Extensions)
        builder.Services.AddAzureAIServices(configuration, builder.Environment);
        builder.Services.AddCoreServices();
        builder.Services.AddSearchAgents(configuration);
        builder.Services.AddDataProcessors(configuration);
        builder.Services.AddDataPipelineServices(configuration);
        builder.Services.AddCachingAndOptimization(configuration);
        builder.Services.AddHealthChecks().AddHealthChecks(configuration);

        // 6. Authentication & Authorization
        var authenticationBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

        // Use a separate logger for startup auth configuration to avoid early BuildServiceProvider
        using var startupLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var startupLogger = startupLoggerFactory.CreateLogger("Program");
        authenticationBuilder.AddDualIssuerJwtBearer(configuration, startupLogger);

        builder.Services.AddMotorcycleRagAuthorization(configuration, builder.Environment);
        builder.Services.AddMotorcycleRagRateLimiting();
    }
}
