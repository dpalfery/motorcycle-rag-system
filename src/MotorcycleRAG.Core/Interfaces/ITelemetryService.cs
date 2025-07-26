namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Provides methods for tracking application telemetry such as queries, cost and custom events.
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Tracks a motorcycle query execution event.
    /// </summary>
    /// <param name="queryId">Identifier of the query.</param>
    /// <param name="query">The query text.</param>
    /// <param name="duration">Total duration of the query processing.</param>
    /// <param name="resultsCount">Number of results returned.</param>
    /// <param name="estimatedCost">Estimated cost of the query (optional).</param>
    /// <param name="correlationId">Correlation identifier for distributed tracing (optional).</param>
    void TrackQuery(string queryId, string query, TimeSpan duration, int resultsCount, decimal estimatedCost = 0m, string? correlationId = null);

    /// <summary>
    /// Tracks cost and usage metrics for a query.
    /// </summary>
    /// <param name="queryId">Identifier of the query.</param>
    /// <param name="estimatedCost">Estimated monetary cost of the operation.</param>
    /// <param name="tokensUsed">Tokens used by the LLM (optional).</param>
    /// <param name="correlationId">Correlation identifier (optional).</param>
    void TrackCost(string queryId, decimal estimatedCost, int tokensUsed = 0, string? correlationId = null);

    /// <summary>
    /// Tracks a custom application event.
    /// </summary>
    /// <param name="eventName">Name of the event.</param>
    /// <param name="properties">Event properties (optional).</param>
    /// <param name="metrics">Event metrics (optional).</param>
    void TrackEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null);
}