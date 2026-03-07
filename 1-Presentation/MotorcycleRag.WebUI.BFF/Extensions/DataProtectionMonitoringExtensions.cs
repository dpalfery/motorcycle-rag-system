using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using System.Diagnostics;

namespace MotorcycleRag.WebUI.BFF.Extensions;

/// <summary>
/// Provides monitoring and telemetry for Data Protection key persistence operations.
/// Tracks metrics, events, and enhances logging with correlation IDs.
/// </summary>
public class DataProtectionMonitoringService
{
    private readonly TelemetryClient _telemetryClient;
    private readonly ILogger<DataProtectionMonitoringService> _logger;

    // Metric names
    public const string MetricKeyPersistenceSuccess = "DataProtection.KeyPersistence.Success";
    public const string MetricKeyPersistenceFailure = "DataProtection.KeyPersistence.Failure";
    public const string MetricKeyPersistenceDuration = "DataProtection.KeyPersistence.Duration";
    
    // Event names
    public const string EventKeysInitialized = "DataProtection.KeysInitialized";
    public const string EventKeysRotated = "DataProtection.KeysRotated";
    public const string EventKeysLoaded = "DataProtection.KeysLoaded";

    // Custom properties
    public const string PropertyBlobUri = "BlobUri";
    public const string PropertyDurationMs = "DurationMs";
    public const string PropertyErrorMessage = "ErrorMessage";
    public const string PropertyCorrelationId = "CorrelationId";

    public DataProtectionMonitoringService(TelemetryClient telemetryClient, ILogger<DataProtectionMonitoringService> logger)
    {
        _telemetryClient = telemetryClient;
        _logger = logger;
    }

    /// <summary>
    /// Tracks successful key persistence operation.
    /// </summary>
    /// <param name="blobUri">The blob URI where keys are persisted</param>
    /// <param name="durationMs">Time taken to persist keys in milliseconds</param>
    /// <param name="correlationId">Optional correlation ID for request tracing</param>
    public void TrackKeyPersistenceSuccess(string blobUri, long durationMs, string? correlationId = null)
    {
        var properties = new Dictionary<string, string?>
        {
            { PropertyBlobUri, GetSanitizedBlobUri(blobUri) },
            { PropertyDurationMs, durationMs.ToString() },
            { PropertyCorrelationId, correlationId ?? Guid.NewGuid().ToString() }
        };

        // Track counter metric
        _telemetryClient.GetMetric(MetricKeyPersistenceSuccess).TrackValue(1);
        
        // Track duration metric
        _telemetryClient.GetMetric(MetricKeyPersistenceDuration).TrackValue(durationMs);

        // Log with correlation ID
        _logger.LogInformation(
            "Data Protection keys persisted successfully to {BlobUri} in {DurationMs}ms. CorrelationId: {CorrelationId}",
            GetSanitizedBlobUri(blobUri), durationMs, properties[PropertyCorrelationId]);
    }

    /// <summary>
    /// Tracks failed key persistence operation.
    /// </summary>
    /// <param name="blobUri">The blob URI where keys were being persisted</param>
    /// <param name="durationMs">Time taken before failure in milliseconds</param>
    /// <param name="errorMessage">The error message</param>
    /// <param name="correlationId">Optional correlation ID for request tracing</param>
    public void TrackKeyPersistenceFailure(string blobUri, long durationMs, string errorMessage, string? correlationId = null)
    {
        var properties = new Dictionary<string, string?>
        {
            { PropertyBlobUri, GetSanitizedBlobUri(blobUri) },
            { PropertyDurationMs, durationMs.ToString() },
            { PropertyErrorMessage, errorMessage },
            { PropertyCorrelationId, correlationId ?? Guid.NewGuid().ToString() }
        };

        // Track counter metric
        _telemetryClient.GetMetric(MetricKeyPersistenceFailure).TrackValue(1);

        // Log error with correlation ID
        _logger.LogError(
            "Data Protection keys failed to persist to {BlobUri} after {DurationMs}ms. Error: {ErrorMessage}. CorrelationId: {CorrelationId}",
            GetSanitizedBlobUri(blobUri), durationMs, errorMessage, properties[PropertyCorrelationId]);
    }

    /// <summary>
    /// Tracks Data Protection keys initialization event.
    /// </summary>
    /// <param name="blobUri">The blob URI where keys are persisted</param>
    /// <param name="correlationId">Optional correlation ID for request tracing</param>
    public void TrackKeysInitialized(string blobUri, string? correlationId = null)
    {
        var properties = new Dictionary<string, string?>
        {
            { PropertyBlobUri, GetSanitizedBlobUri(blobUri) },
            { PropertyCorrelationId, correlationId ?? Guid.NewGuid().ToString() }
        };

        _telemetryClient.TrackEvent(EventKeysInitialized, properties);

        _logger.LogInformation(
            "Data Protection keys initialized and persisted to {BlobUri}. CorrelationId: {CorrelationId}",
            GetSanitizedBlobUri(blobUri), properties[PropertyCorrelationId]);
    }

    /// <summary>
    /// Tracks Data Protection keys rotation event.
    /// </summary>
    /// <param name="blobUri">The blob URI where keys are persisted</param>
    /// <param name="correlationId">Optional correlation ID for request tracing</param>
    public void TrackKeysRotated(string blobUri, string? correlationId = null)
    {
        var properties = new Dictionary<string, string?>
        {
            { PropertyBlobUri, GetSanitizedBlobUri(blobUri) },
            { PropertyCorrelationId, correlationId ?? Guid.NewGuid().ToString() }
        };

        _telemetryClient.TrackEvent(EventKeysRotated, properties);

        _logger.LogInformation(
            "Data Protection keys rotated and persisted to {BlobUri}. CorrelationId: {CorrelationId}",
            GetSanitizedBlobUri(blobUri), properties[PropertyCorrelationId]);
    }

    /// <summary>
    /// Tracks Data Protection keys loaded from blob storage event.
    /// </summary>
    /// <param name="blobUri">The blob URI where keys were loaded from</param>
    /// <param name="correlationId">Optional correlation ID for request tracing</param>
    public void TrackKeysLoaded(string blobUri, string? correlationId = null)
    {
        var properties = new Dictionary<string, string?>
        {
            { PropertyBlobUri, GetSanitizedBlobUri(blobUri) },
            { PropertyCorrelationId, correlationId ?? Guid.NewGuid().ToString() }
        };

        _telemetryClient.TrackEvent(EventKeysLoaded, properties);

        _logger.LogInformation(
            "Data Protection keys loaded from {BlobUri}. CorrelationId: {CorrelationId}",
            GetSanitizedBlobUri(blobUri), properties[PropertyCorrelationId]);
    }

    /// <summary>
    /// Creates a stopwatch for measuring key persistence duration.
    /// </summary>
    public static Stopwatch StartKeyPersistenceStopwatch() => Stopwatch.StartNew();

    /// <summary>
    /// Sanitizes the blob URI to avoid exposing sensitive information in telemetry.
    /// Removes account key if present and normalizes the URI.
    /// </summary>
    private static string GetSanitizedBlobUri(string blobUri)
    {
        if (string.IsNullOrEmpty(blobUri))
            return "not-configured";

        try
        {
            var uri = new Uri(blobUri);
            // Return only the host and container path, not any SAS tokens or keys
            var containerPath = string.Join("", uri.Segments.Skip(1));
            return $"{uri.Host}/{containerPath.TrimEnd('/')}";
        }
        catch
        {
            return "invalid-uri";
        }
    }
}

/// <summary>
/// Extension methods for registering Data Protection monitoring services.
/// </summary>
public static class DataProtectionMonitoringExtensions
{
    /// <summary>
    /// Adds Data Protection monitoring services to the service collection.
    /// </summary>
    public static IServiceCollection AddDataProtectionMonitoring(this IServiceCollection services)
    {
        services.AddSingleton<DataProtectionMonitoringService>();
        return services;
    }
}
