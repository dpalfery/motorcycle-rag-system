using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.ApplicationInsights;
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
        private readonly MotorcycleRAG.Core.Options.TelemetryOptions _telemetryConfig;
        private readonly SqlOptions _sqlOptions;
        
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
        /// <param name="telemetryConfig">Telemetry configuration</param>
        /// <param name="sqlOptions">SQL options</param>
        public TelemetryService(
            TelemetryClient telemetryClient,
            ILogger<TelemetryService> logger,
            IOptions<MotorcycleRAG.Core.Options.TelemetryOptions> telemetryConfig,
            IOptions<SqlOptions> sqlOptions)
        {
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _telemetryConfig = telemetryConfig?.Value ?? throw new ArgumentNullException(nameof(telemetryConfig));
            _sqlOptions = sqlOptions?.Value ?? throw new ArgumentNullException(nameof(sqlOptions));
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
                _telemetryClient.TrackEvent(eventName, redactedProperties, metrics);
                
                _logger.LogDebug("Tracked event: {EventName}", eventName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track event: {EventName}", eventName);
                throw;
            }
        }

        /// <summary>
        /// Tracks an exception
        /// </summary>
        /// <param name="exception">Exception to track</param>
        /// <param name="properties">Additional properties</param>
        public void TrackException(Exception exception, Dictionary<string, string>? properties = null)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            try
            {
                var redactedProperties = RedactSensitiveData(properties);
                _telemetryClient.TrackException(exception, redactedProperties);
                
                _logger.LogError(exception, "Tracked exception");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track exception");
                throw;
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
                _telemetryClient.TrackMetric(metricName, value, redactedProperties);
                
                _logger.LogDebug("Tracked metric: {MetricName} = {Value}", metricName, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track metric: {MetricName}", metricName);
                throw;
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
                _telemetryClient.TrackRequest(name, startTime, duration, responseCode, success);
                
                _logger.LogDebug("Tracked request: {RequestName}, success: {Success}, duration: {Duration}ms",
                    name, success, duration.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track request: {RequestName}", name);
                throw;
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
                
                // Track as custom event
                _telemetryClient.TrackEvent("QueryExecuted", properties);
                
                _logger.LogDebug("Tracked query: {QueryId}, duration: {Duration}ms, results: {ResultsCount}",
                    queryId, duration.TotalMilliseconds, resultsCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track query: {QueryId}", queryId);
                throw;
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
                    if (IsSensitivePropertyName(kvp.Key))
                    {
                        redactedProperties[kvp.Key] = "[REDACTED]";
                    }
                    else
                    {
                        redactedProperties[kvp.Key] = RedactSensitiveData(kvp.Value);
                    }
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

            var lowerPropertyName = propertyName.ToLowerInvariant();
            return lowerPropertyName.Contains("password") ||
                   lowerPropertyName.Contains("secret") ||
                   lowerPropertyName.Contains("key") ||
                   lowerPropertyName.Contains("token") ||
                   lowerPropertyName.Contains("connection") ||
                   lowerPropertyName.Contains("credential");
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
                throw;
            }
        }
    }
}