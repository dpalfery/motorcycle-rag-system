using Microsoft.ApplicationInsights;
using MotorcycleRAG.Core.Interfaces;

namespace MotorcycleRAG.Infrastructure.Telemetry;

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
    public void TrackQuery(string queryId, string query, TimeSpan duration, int resultsCount, decimal estimatedCost = 0m, string? correlationId = null)
    {
        correlationId ??= _correlationService.GetOrCreateCorrelationId();

        var properties = new Dictionary<string, string>
        {
            ["QueryId"] = queryId,
            ["Query"] = query,
            ["CorrelationId"] = correlationId
        };

        var metrics = new Dictionary<string, double>
        {
            ["DurationMs"] = duration.TotalMilliseconds,
            ["ResultsCount"] = resultsCount
        };

        if (estimatedCost > 0)
        {
            metrics["EstimatedCost"] = (double)estimatedCost;
        }

        _telemetryClient.TrackEvent("MotorcycleQuery", properties, metrics);
    }

    /// <inheritdoc />
    public void TrackCost(string queryId, decimal estimatedCost, int tokensUsed = 0, string? correlationId = null)
    {
        correlationId ??= _correlationService.GetOrCreateCorrelationId();

        var properties = new Dictionary<string, string>
        {
            ["QueryId"] = queryId,
            ["CorrelationId"] = correlationId
        };

        var metrics = new Dictionary<string, double>
        {
            ["EstimatedCost"] = (double)estimatedCost
        };

        if (tokensUsed > 0)
        {
            metrics["TokensUsed"] = tokensUsed;
        }

        _telemetryClient.TrackEvent("QueryCost", properties, metrics);
    }

    /// <inheritdoc />
    public void TrackEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null)
    {
        properties ??= new();
        if (!properties.ContainsKey("CorrelationId"))
        {
            properties["CorrelationId"] = _correlationService.GetOrCreateCorrelationId();
        }

        _telemetryClient.TrackEvent(eventName, properties, metrics);
    }
}