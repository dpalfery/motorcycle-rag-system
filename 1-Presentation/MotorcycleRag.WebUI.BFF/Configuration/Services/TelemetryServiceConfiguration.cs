namespace MotorcycleRag.WebUI.BFF.Configuration.Services;

/// <summary>
/// Configuration for telemetry services in the BFF.
/// </summary>
internal static class TelemetryServiceConfiguration
{
    public static IServiceCollection AddBffTelemetry(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        var appInsightsConnectionString = configuration.GetConnectionString("ApplicationInsights")
            ?? configuration["ApplicationInsights:ConnectionString"];

        if (!string.IsNullOrEmpty(appInsightsConnectionString))
        {
            services.AddApplicationInsightsTelemetry(options =>
            {
                options.ConnectionString = appInsightsConnectionString;
                options.EnableQuickPulseMetricStream = true;
            });
        }

        return services;
    }
}
