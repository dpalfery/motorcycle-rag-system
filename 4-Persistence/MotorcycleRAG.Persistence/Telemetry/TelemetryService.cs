using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options; 


namespace MotorcycleRAG.Persistence.Telemetry
{
    /// <summary>
    /// Telemetry service for logging with redaction capabilities
    /// </summary>
    public class TelemetryService : ITelemetryService
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger<TelemetryService> _logger;
        private readonly Action<ITelemetry>? _telemetryObserver;

        // Regex patterns for sensitive data detection
        private readonly Regex _queryTextPattern = new Regex(@"(SELECT|INSERT|UPDATE|DELETE).*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly Regex _connectionStringPattern = new Regex(@"(Server|Database|User ID|Password|Pwd)=[^;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly Regex _apiKeyPattern = new Regex(@"api[_-]?key[_-]?[=:][^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly Regex _secretPattern = new Regex(@"secret[_-]?[=:][^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Initializes a new instance of the TelemetryService
        /// </summary>
        /// <param name="telemetryClient">Telemetry client</param>
        /// <param name="logger">Logger</param>
        public TelemetryService(
            TelemetryClient telemetryClient,
            ILogger<TelemetryService> logger)
            : this(telemetryClient, logger, telemetryObserver: null)
        {
        }

        internal TelemetryService(
            TelemetryClient telemetryClient,
            ILogger<TelemetryService> logger,
            Action<ITelemetry>? telemetryObserver)
        {
            ArgumentNullException.ThrowIfNull(telemetryClient);
            ArgumentNullException.ThrowIfNull(logger);

            _telemetryClient = telemetryClient;
            _logger = logger;
            _telemetryObserver = telemetryObserver;
        }

        /// <summary>
        /// Tracks an event
        /// </summary>
        /// <param name="eventName">Event name</param>
        /// <param name="properties">Event properties</param>
        /// <param name="metrics">Event metrics</param>
        public void TrackEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                throw new ArgumentException("Event name cannot be null or empty", nameof(eventName));
            }

            try
            {
                var redactedProperties = RedactSensitiveData(properties);
                var telemetry = new EventTelemetry(eventName);
                AddProperties(telemetry.Properties, redactedProperties);
                Track(telemetry);
                TrackMetrics(metrics, redactedProperties);
                
                _logger.LogDebug("Tracked event: {EventName}", eventName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track event: {EventName}", eventName);
                throw new InvalidOperationException($"Failed to track event: {eventName}", ex);
            }
        }

        /// <summary>
        /// Tracks an exception
        /// </summary>
        /// <param name="exception">Exception to track</param>
        /// <param name="properties">Additional properties</param>
        public void TrackException(Exception exception, Dictionary<string, string>? properties = null)
        {
            ArgumentNullException.ThrowIfNull(exception);

            try
            {
                var redactedProperties = RedactSensitiveData(properties);
                var telemetry = new ExceptionTelemetry(exception);
                AddProperties(telemetry.Properties, redactedProperties);
                Track(telemetry);
                
                _logger.LogError(exception, "Tracked exception");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track {ExceptionType}", ex.GetType().Name);
                throw new InvalidOperationException($"Failed to track {nameof(exception)}", ex);
            }
        }

        /// <summary>
        /// Tracks a metric
        /// </summary>
        /// <param name="metricName">Metric name</param>
        /// <param name="value">Metric value</param>
        /// <param name="properties">Additional properties</param>
        public void TrackMetric(string metricName, double value, Dictionary<string, string>? properties = null)
        {
            if (string.IsNullOrWhiteSpace(metricName))
            {
                throw new ArgumentException("Metric name cannot be null or empty", nameof(metricName));
            }

            try
            {
                var redactedProperties = RedactSensitiveData(properties);
                var telemetry = new MetricTelemetry(metricName, value);
                AddProperties(telemetry.Properties, redactedProperties);
                Track(telemetry);
                
                _logger.LogDebug("Tracked metric: {MetricName} = {Value}", metricName, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track metric: {MetricName}", metricName);
                throw new InvalidOperationException($"Failed to track metric: {metricName}", ex);
            }
        }

        /// <summary>
        /// Tracks a request
        /// </summary>
        /// <param name="name">Request name</param>
        /// <param name="startTime">Request start time</param>
        /// <param name="duration">Request duration</param>
        /// <param name="responseCode">Response code</param>
        /// <param name="success">Whether the request was successful</param>
        public void TrackRequest(string name, DateTimeOffset startTime, TimeSpan duration, string responseCode, bool success)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Request name cannot be null or empty", nameof(name));
            }

            try
            {
                var telemetry = new RequestTelemetry(name, startTime, duration, responseCode, success);
                Track(telemetry);
                
                _logger.LogDebug("Tracked request: {RequestName}, success: {Success}, duration: {Duration}ms",
                    name, success, duration.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track request: {RequestName}", name);
                throw new InvalidOperationException($"Failed to track request: {name}", ex);
            }
        }

        /// <summary>
        /// Tracks a query operation
        /// </summary>
        /// <param name="queryId">Query ID</param>
        /// <param name="query">Query text</param>
        /// <param name="duration">Query duration</param>
        /// <param name="resultsCount">Number of results</param>
        /// <param name="estimatedCost">Estimated cost</param>
        public void TrackQuery(string queryId, string query, TimeSpan duration, int resultsCount, decimal estimatedCost)
        {
            if (string.IsNullOrWhiteSpace(queryId))
            {
                throw new ArgumentException("Query ID cannot be null or empty", nameof(queryId));
            }

            try
            {
                // Redact sensitive data from query text
                var redactedQuery = RedactSensitiveData(query);
                
                // Create properties dictionary
                var properties = new Dictionary<string, string>
                {
                    ["queryId"] = queryId,
                    ["redactedQuery"] = redactedQuery,
                    ["resultsCount"] = resultsCount.ToString(),
                    ["estimatedCost"] = estimatedCost.ToString("F4")
                };
                
                var telemetry = new EventTelemetry("QueryExecuted");
                AddProperties(telemetry.Properties, properties);
                Track(telemetry);
                
                _logger.LogDebug("Tracked query: {QueryId}, duration: {Duration}ms, results: {ResultsCount}",
                    queryId, duration.TotalMilliseconds, resultsCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track query: {QueryId}", queryId);
                throw new InvalidOperationException($"Failed to track query: {queryId}", ex);
            }
        }

        /// <summary>
        /// Redacts sensitive data from a string
        /// </summary>
        /// <param name="input">Input string</param>
        /// <returns>Redacted string</returns>
        private string RedactSensitiveData(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return input;
            }

            try
            {
                // Redact SQL query text
                var redacted = _queryTextPattern.Replace(input, match => 
                {
                    var query = match.Value;
                    if (query.Length > 50)
                    {
                        return $"{query[..20]}...{query[^20..]}";
                    }
                    return "[REDACTED_QUERY]";
                });

                // Redact connection strings
                redacted = _connectionStringPattern.Replace(redacted, "[REDACTED_CONNECTION_STRING]");

                // Redact API keys
                redacted = _apiKeyPattern.Replace(redacted, "[REDACTED_API_KEY]");

                // Redact secrets
                redacted = _secretPattern.Replace(redacted, "[REDACTED_SECRET]");

                return redacted;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to redact sensitive data, returning original string");
                return input;
            }
        }

        /// <summary>
        /// Redacts sensitive data from a dictionary of properties
        /// </summary>
        /// <param name="properties">Input properties</param>
        /// <returns>Redacted properties</returns>
        private System.Collections.Generic.IDictionary<string, string>? RedactSensitiveData(System.Collections.Generic.IDictionary<string, string>? properties)
        {
            if (properties == null || properties.Count == 0)
            {
                return properties;
            }

            try
            {
                var redactedProperties = new System.Collections.Generic.Dictionary<string, string>();
                foreach (var kvp in properties)
                {
                    // Skip known sensitive property names
                    redactedProperties[kvp.Key] = IsSensitivePropertyName(kvp.Key) ? "[REDACTED]" : RedactSensitiveData(kvp.Value);
                }
                return redactedProperties;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to redact sensitive properties, returning original properties");
                return properties;
            }
        }

        /// <summary>
        /// Checks if a property name is considered sensitive
        /// </summary>
        /// <param name="propertyName">Property name</param>
        /// <returns>True if sensitive, false otherwise</returns>
        private bool IsSensitivePropertyName(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            var upperPropertyName = propertyName.ToUpperInvariant();
            return upperPropertyName.Contains("PASSWORD") ||
                   upperPropertyName.Contains("SECRET") ||
                   upperPropertyName.Contains("KEY") ||
                   upperPropertyName.Contains("TOKEN") ||
                   upperPropertyName.Contains("CONNECTION") ||
                   upperPropertyName.Contains("CREDENTIAL");
        }

        /// <summary>
        /// Flushes the telemetry client
        /// </summary>
        public void Flush()
        {
            try
            {
                _telemetryClient.Flush();
                _logger.LogDebug("Telemetry client flushed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush telemetry client");
                throw new InvalidOperationException("Failed to flush telemetry client", ex);
            }
        }

        /// <inheritdoc />
        public void TrackDegradedMode(
            string correlationId,
            IReadOnlyList<string> failedSources,
            IReadOnlyList<string> availableSources,
            TimeSpan duration,
            int resultsFound)
        {
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                throw new ArgumentException("Correlation ID cannot be null or empty", nameof(correlationId));
            }

            ArgumentNullException.ThrowIfNull(failedSources);
            ArgumentNullException.ThrowIfNull(availableSources);

            try
            {
                var properties = new Dictionary<string, string>
                {
                    ["CorrelationId"] = correlationId,
                    ["FailedSources"] = string.Join(",", failedSources),
                    ["AvailableSources"] = string.Join(",", availableSources),
                    ["FailureCount"] = failedSources.Count.ToString(),
                    ["AvailableSourceCount"] = availableSources.Count.ToString()
                };

                var metrics = new Dictionary<string, double>
                {
                    ["DurationMs"] = duration.TotalMilliseconds,
                    ["ResultsFound"] = resultsFound
                };

                var telemetry = new EventTelemetry("SearchDegradedMode");
                AddProperties(telemetry.Properties, properties);
                Track(telemetry);
                TrackMetrics(metrics, properties);

                _logger.LogWarning("Tracked degraded mode operation: CorrelationId={CorrelationId}, FailedSources={FailedSources}, " +
                    "AvailableSources={AvailableSources}, Duration={Duration}ms, Results={Results}",
                    correlationId, string.Join(",", failedSources), string.Join(",", availableSources),
                    duration.TotalMilliseconds, resultsFound);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track degraded mode: {CorrelationId}", correlationId);
                throw new InvalidOperationException($"Failed to track degraded mode: {correlationId}", ex);
            }
        }

        /// <inheritdoc />
        public void TrackSourceFailure(string correlationId, string sourceName, string errorMessage, TimeSpan duration)
        {
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                throw new ArgumentException("Correlation ID cannot be null or empty", nameof(correlationId));
            }

            if (string.IsNullOrWhiteSpace(sourceName))
            {
                throw new ArgumentException("Source name cannot be null or empty", nameof(sourceName));
            }

            try
            {
                var redactedErrorMessage = RedactSensitiveData(errorMessage);

                var properties = new Dictionary<string, string>
                {
                    ["CorrelationId"] = correlationId,
                    ["SourceName"] = sourceName,
                    ["ErrorMessage"] = redactedErrorMessage ?? "Unknown error"
                };

                var metrics = new Dictionary<string, double>
                {
                    ["DurationMs"] = duration.TotalMilliseconds
                };

                var telemetry = new EventTelemetry("SourceFailure");
                AddProperties(telemetry.Properties, properties);
                Track(telemetry);
                TrackMetrics(metrics, properties);
                
                _logger.LogWarning("Tracked source failure: CorrelationId={CorrelationId}, Source={Source}, " +
                    "Error={Error}, Duration={Duration}ms",
                    correlationId, sourceName, redactedErrorMessage ?? "[Unknown error]", duration.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track source failure: {CorrelationId}, {Source}", correlationId, sourceName);
                throw new InvalidOperationException($"Failed to track source failure: {correlationId}, {sourceName}", ex);
            }
        }

        /// <inheritdoc />
        public void TrackSearchExecution(string correlationId, string queryId, TimeSpan totalDuration, int totalResults, int successfulSources, int failedSources, bool degradedMode)
        {
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                throw new ArgumentException("Correlation ID cannot be null or empty", nameof(correlationId));
            }

            if (string.IsNullOrWhiteSpace(queryId))
            {
                throw new ArgumentException("Query ID cannot be null or empty", nameof(queryId));
            }

            try
            {
                var properties = new Dictionary<string, string>
                {
                    ["CorrelationId"] = correlationId,
                    ["QueryId"] = queryId,
                    ["SuccessfulSources"] = successfulSources.ToString(),
                    ["FailedSources"] = failedSources.ToString(),
                    ["DegradedMode"] = degradedMode.ToString()
                };

                var metrics = new Dictionary<string, double>
                {
                    ["TotalDurationMs"] = totalDuration.TotalMilliseconds,
                    ["TotalResults"] = totalResults,
                    ["SourceSuccessRate"] = successfulSources > 0 ? (successfulSources / (double)(successfulSources + failedSources)) * 100 : 0
                };

                var telemetry = new EventTelemetry("SearchExecution");
                AddProperties(telemetry.Properties, properties);
                Track(telemetry);
                TrackMetrics(metrics, properties);
                
                _logger.LogInformation("Tracked search execution: CorrelationId={CorrelationId}, QueryId={QueryId}, " +
                    "Duration={Duration}ms, Results={Results}, SuccessfulSources={SuccessfulSources}, " +
                    "FailedSources={FailedSources}, DegradedMode={DegradedMode}",
                    correlationId, queryId, totalDuration.TotalMilliseconds, totalResults, successfulSources, failedSources, degradedMode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track search execution: {CorrelationId}, {QueryId}", correlationId, queryId);
                throw new InvalidOperationException($"Failed to track search execution: {correlationId}, {queryId}", ex);
            }
        }

        /// <inheritdoc />
        public void TrackOnboardingTransition(
            string requestId,
            string stage,
            string state,
            string correlationId,
            TimeSpan duration)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
            ArgumentException.ThrowIfNullOrWhiteSpace(stage);
            ArgumentException.ThrowIfNullOrWhiteSpace(state);

            var properties = new Dictionary<string, string>
            {
                ["RequestId"] = requestId,
                ["Stage"] = stage,
                ["State"] = state,
                ["CorrelationId"] = string.IsNullOrWhiteSpace(correlationId) ? "unknown" : correlationId
            };

            var metrics = new Dictionary<string, double>
            {
                ["DurationMs"] = duration.TotalMilliseconds
            };

            TrackEvent("OnboardingTransition", properties, metrics);
            TrackMetric("OnboardingStageDurationMs", duration.TotalMilliseconds, properties);
        }

        /// <inheritdoc />
        public void TrackAdminAction(
            string action,
            string targetId,
            string? actorUserId,
            bool success,
            TimeSpan duration)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(action);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

            var properties = new Dictionary<string, string>
            {
                ["Action"] = action,
                ["TargetId"] = targetId,
                ["ActorUserId"] = string.IsNullOrWhiteSpace(actorUserId) ? "unknown" : actorUserId,
                ["Success"] = success.ToString()
            };

            var metrics = new Dictionary<string, double>
            {
                ["DurationMs"] = duration.TotalMilliseconds
            };

            TrackEvent("AdminUserManagementAction", properties, metrics);
            TrackMetric("AdminActionDurationMs", duration.TotalMilliseconds, properties);
        }

        /// <inheritdoc />
        public void TrackDependencyDegradation(string dependencyName, string correlationId, string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dependencyName);
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);

            var properties = new Dictionary<string, string>
            {
                ["DependencyName"] = dependencyName,
                ["CorrelationId"] = string.IsNullOrWhiteSpace(correlationId) ? "unknown" : correlationId,
                ["Reason"] = reason
            };

            TrackEvent("OnboardingDependencyDegraded", properties);
        }

        private void Track(ITelemetry telemetry)
        {
            _telemetryObserver?.Invoke(telemetry);
            _telemetryClient.Track(telemetry);
        }

        private static void AddProperties(
            IDictionary<string, string> telemetryProperties,
            IDictionary<string, string>? properties)
        {
            if (properties == null)
            {
                return;
            }

            foreach (var property in properties)
            {
                telemetryProperties[property.Key] = property.Value;
            }
        }

        private void TrackMetrics(
            IDictionary<string, double>? metrics,
            IDictionary<string, string>? properties)
        {
            if (metrics == null)
            {
                return;
            }

            foreach (var metric in metrics)
            {
                var telemetry = new MetricTelemetry(metric.Key, metric.Value);
                AddProperties(telemetry.Properties, properties);
                Track(telemetry);
            }
        }
    }
}
