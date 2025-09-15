using Microsoft.ApplicationInsights;
using MotorcycleRAG.Contracts.Interfaces;

namespace MotorcycleRAG.Persistence.Telemetry;

/// <summary>
/// Application Insights based implementation of <see cref="ITelemetryService"/>.
/// </summary>
public sealed class TelemetryService : ITelemetryService
{
    private readonly TelemetryClient _telemetryClient;
    private readonly ICorrelationService _correlationService;

    public TelemetryService(TelemetryClient telemetryClient, ICorrelationService correlationService)
    {
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        _correlationService = correlationService ?? throw new ArgumentNullException(nameof(correlationService));
    }

    /// <inheritdoc />
    public void TrackEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null)
    {
        properties ??= new();
        if (!properties.ContainsKey("CorrelationId"))
        {
            properties["CorrelationId"] = _correlationService.GetCorrelationId();
        }

        _telemetryClient.TrackEvent(eventName, properties, metrics);
    }

    /// <inheritdoc />
    public void TrackException(Exception exception, Dictionary<string, string>? properties = null)
    {
        properties ??= new();
        if (!properties.ContainsKey("CorrelationId"))
        {
            properties["CorrelationId"] = _correlationService.GetCorrelationId();
        }

        _telemetryClient.TrackException(exception, properties);
    }

    /// <inheritdoc />
    public void TrackMetric(string metricName, double value, Dictionary<string, string>? properties = null)
    {
        properties ??= new();
        if (!properties.ContainsKey("CorrelationId"))
        {
            properties["CorrelationId"] = _correlationService.GetCorrelationId();
        }

        _telemetryClient.TrackMetric(metricName, value, properties);
    }

    /// <inheritdoc />
    public void TrackRequest(string name, DateTimeOffset startTime, TimeSpan duration, string responseCode, bool success)
    {
        var properties = new Dictionary<string, string>
        {
            ["CorrelationId"] = _correlationService.GetCorrelationId()
        };

        _telemetryClient.TrackRequest(name, startTime, duration, responseCode, success);
        _telemetryClient.TrackEvent("Request", properties);
    }

    /// <inheritdoc />
    public void TrackQuery(string queryId, string query, TimeSpan duration, int resultsCount, decimal estimatedCost)
    {
        var properties = new Dictionary<string, string>
        {
            ["QueryId"] = queryId,
            ["Query"] = query,
            ["CorrelationId"] = _correlationService.GetCorrelationId()
        };

        var metrics = new Dictionary<string, double>
        {
            ["DurationMs"] = duration.TotalMilliseconds,
            ["ResultsCount"] = resultsCount,
            ["EstimatedCost"] = (double)estimatedCost
        };

        _telemetryClient.TrackEvent("MotorcycleQuery", properties, metrics);
    }
}
