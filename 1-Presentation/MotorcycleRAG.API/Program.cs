using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.API.Configuration.Services;
using MotorcycleRAG.API.Extensions;
using MotorcycleRAG.API.Services;
using MotorcycleRAG.Application.Extensions;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Contracts.Interfaces;

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
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var configuration = builder.Configuration;

        // 1. Core Configuration & Logging
        builder.Logging.AddStructuredLogging(builder.Environment);
        builder.AddAzureAppConfigurationWithKeyVault();

        // 2. Telemetry & Monitoring
        builder.Services.AddMotorcycleRagTelemetry(configuration);

        // 3. MVC & Core Services
        builder.Services.AddControllers();
        
        // Enforce maximum request body size (50MB) for security and DoS mitigation
        builder.WebHost.ConfigureKestrel(options => {
            options.Limits.MaxRequestBodySize = 50 * 1024 * 1024;
        });

        builder.Services.Configure<FormOptions>(options => {
            options.MultipartBodyLengthLimit = 50 * 1024 * 1024;
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
        builder.Services.ConfigureJsonSerialization(builder.Environment.IsDevelopment());
        builder.Services.Configure<AppOptions>(configuration);

        // 4. API Documentation & Connectivity
        builder.Services.AddApiDocumentation();
        builder.Services.AddRestrictedCors(configuration);
        
        // Disable built-in HostFilter in favor of custom HostHeaderValidationMiddleware
        builder.Services.PostConfigure<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>(options =>
        {
            options.AllowedHosts = ["*"];
        });

        // 5. Domain & Infrastructure Services (Existing Extensions)
        builder.Services.AddAzureAIServices(configuration);
        builder.Services.AddCoreServices();
        builder.Services.AddSearchAgents(configuration);
        builder.Services.AddDataProcessors(configuration);
        builder.Services.AddDataPipelineServices(configuration);
        builder.Services.AddCachingAndOptimization(configuration);
        builder.Services.AddSqlPersistence(configuration);
        builder.Services.AddWebTrustPolicyServices(configuration);
        builder.Services.AddHealthChecks().AddHealthChecks(configuration);

        // 6. Authentication & Authorization
        var authenticationBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
        
        // Use a separate logger for startup auth configuration to avoid early BuildServiceProvider
        using var startupLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var startupLogger = startupLoggerFactory.CreateLogger("Program");
        authenticationBuilder.AddDualIssuerJwtBearer(configuration, startupLogger);

        builder.Services.AddMotorcycleRagAuthorization(configuration, builder.Environment);
        builder.Services.AddMotorcycleRagRateLimiting();

        // 7. Build and Configure Pipeline
        var app = builder.Build();

        app.UseMotorcycleRagMiddleware();
        
        await app.PreWarmJwtSigningKeysAsync();
        await app.RunAsync();
    }
}
