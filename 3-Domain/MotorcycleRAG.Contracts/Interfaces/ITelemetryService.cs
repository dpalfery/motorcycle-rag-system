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

    /// <summary>
    /// Tracks when the system operates in degraded mode due to source failures
    /// </summary>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <param name="failedSources">List of failed source names</param>
    /// <param name="availableSources">List of available source names</param>
    /// <param name="duration">Duration of the degraded operation</param>
    /// <param name="resultsFound">Number of results obtained from partial sources</param>
    void TrackDegradedMode(string correlationId, IReadOnlyList<string> failedSources, IReadOnlyList<string> availableSources, TimeSpan duration, int resultsFound);

    /// <summary>
    /// Tracks a source failure during search operations
    /// </summary>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <param name="sourceName">Name of the failed source</param>
    /// <param name="errorMessage">Error message describing the failure</param>
    /// <param name="duration">Duration until failure</param>
    void TrackSourceFailure(string correlationId, string sourceName, string errorMessage, TimeSpan duration);

    /// <summary>
    /// Tracks search execution with source metrics
    /// </summary>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <param name="queryId">Query ID</param>
    /// <param name="totalDuration">Total search execution duration</param>
    /// <param name="totalResults">Total results collected</param>
    /// <param name="successfulSources">Number of successful sources</param>
    /// <param name="failedSources">Number of failed sources</param>
    /// <param name="degradedMode">Whether operating in degraded mode</param>
    void TrackSearchExecution(string correlationId, string queryId, TimeSpan totalDuration, int totalResults, int successfulSources, int failedSources, bool degradedMode);

    /// <summary>
    /// Tracks an onboarding state transition or stage outcome.
    /// </summary>
    void TrackOnboardingTransition(
        string requestId,
        string stage,
        string state,
        string correlationId,
        TimeSpan duration);

    /// <summary>
    /// Tracks an admin user-management action outcome.
    /// </summary>
    void TrackAdminAction(
        string action,
        string targetId,
        string? actorUserId,
        bool success,
        TimeSpan duration);

    /// <summary>
    /// Tracks degraded behavior for an onboarding dependency.
    /// </summary>
    void TrackDependencyDegradation(string dependencyName, string correlationId, string reason);
}
