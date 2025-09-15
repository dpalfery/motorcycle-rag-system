namespace MotorcycleRAG.Contracts.Interfaces;


/// <summary>
/// Interface for telemetry service operations
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Tracks an event
    /// </summary>
    void TrackEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null);

    /// <summary>
    /// Tracks an exception
    /// </summary>
    void TrackException(Exception exception, Dictionary<string, string>? properties = null);

    /// <summary>
    /// Tracks a metric
    /// </summary>
    void TrackMetric(string metricName, double value, Dictionary<string, string>? properties = null);

    /// <summary>
    /// Tracks a request
    /// </summary>
    void TrackRequest(string name, DateTimeOffset startTime, TimeSpan duration, string responseCode, bool success);

    /// <summary>
    /// Tracks a query operation
    /// </summary>
    void TrackQuery(string queryId, string query, TimeSpan duration, int resultsCount, decimal estimatedCost);
}
