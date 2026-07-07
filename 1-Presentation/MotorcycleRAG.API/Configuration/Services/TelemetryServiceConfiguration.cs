using Microsoft.Extensions.DependencyInjection;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using MotorcycleRAG.API.Services;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for telemetry and monitoring services.
/// </summary>
internal static class TelemetryServiceConfiguration
{
    public static IServiceCollection AddMotorcycleRagTelemetry(
        this IServiceCollection services,
        TelemetryOptions options)
    {
        // Fail fast if telemetry is enabled but connection string is not configured
        if (options.EnableTelemetry && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "Application Insights is enabled (EnableTelemetry=true) but ConnectionString is not configured. " +
                "Provide ApplicationInsights:ConnectionString through Azure App Configuration with a Key Vault reference.");
        }

        // Add Application Insights telemetry only if connection string is provided
        if (!string.IsNullOrEmpty(options.ConnectionString))
        {
            services.AddApplicationInsightsTelemetry(aiOptions =>
            {
                aiOptions.ConnectionString = options.ConnectionString;
                aiOptions.ApplicationVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "Unknown";
                aiOptions.EnableQuickPulseMetricStream = true;
                aiOptions.EnablePerformanceCounterCollectionModule = options.EnablePerformanceCounters;
            });
        }
        else
        {
#pragma warning disable CA2000 // Dispose objects before losing scope — registered as singleton; DI container handles disposal
            var config = new TelemetryConfiguration { DisableTelemetry = true };
#pragma warning restore CA2000
            services.AddSingleton(config);
            services.AddSingleton(new TelemetryClient(config));
        }

        return services;
    }
}
