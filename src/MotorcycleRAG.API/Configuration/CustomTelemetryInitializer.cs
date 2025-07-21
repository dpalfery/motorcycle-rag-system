using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Custom telemetry initializer for Application Insights
/// </summary>
public class CustomTelemetryInitializer : ITelemetryInitializer
{
    private readonly IConfiguration _configuration;

    public CustomTelemetryInitializer(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Initialize(ITelemetry telemetry)
    {
        // Add custom properties to all telemetry
        telemetry.Context.GlobalProperties["ApplicationName"] = _configuration.GetValue<string>("ApplicationInsights:ApplicationName", "MotorcycleRAG");
        telemetry.Context.GlobalProperties["Environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown";
        telemetry.Context.GlobalProperties["Version"] = GetType().Assembly.GetName().Version?.ToString() ?? "Unknown";
        
        // Add correlation ID if available
        if (telemetry.Context.Operation.Id == null)
        {
            telemetry.Context.Operation.Id = Guid.NewGuid().ToString();
        }
    }
}