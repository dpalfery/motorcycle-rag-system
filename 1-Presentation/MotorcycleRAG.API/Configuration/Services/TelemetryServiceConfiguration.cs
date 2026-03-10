using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.API.Services;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for telemetry and monitoring services.
/// </summary>
internal static class TelemetryServiceConfiguration
{
    public static IServiceCollection AddMotorcycleRagTelemetry(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        // Validate Application Insights configuration early
        var appInsightsSection = configuration.GetSection("ApplicationInsights");
        var enableTelemetry = appInsightsSection.GetValue<bool>("EnableTelemetry", false);
        var appInsightsConnectionString = configuration.GetConnectionString("ApplicationInsights");

        // Fail fast if telemetry is enabled but connection string is not configured
        if (enableTelemetry && string.IsNullOrWhiteSpace(appInsightsConnectionString))
        {
            throw new InvalidOperationException(
                "Application Insights is enabled (EnableTelemetry=true) but ConnectionString is not configured. " +
                "For local development, use: dotnet user-secrets set \"ConnectionStrings:ApplicationInsights\" \"your-connection-string\" " +
                "--project 1-Presentation/MotorcycleRAG.API");
        }

        // Add Application Insights telemetry only if connection string is provided
        if (!string.IsNullOrEmpty(appInsightsConnectionString))
        {
            services.AddApplicationInsightsTelemetry(options =>
            {
                options.ConnectionString = appInsightsConnectionString;
                options.EnableAdaptiveSampling = true;
                options.EnableQuickPulseMetricStream = true;
                options.EnablePerformanceCounterCollectionModule = configuration.GetValue<bool>("ApplicationInsights:EnablePerformanceCounters", true);
            });

            // Add custom telemetry initializer
            services.AddSingleton<Microsoft.ApplicationInsights.Extensibility.ITelemetryInitializer, CustomTelemetryInitializer>();
        }

        return services;
    }
}
