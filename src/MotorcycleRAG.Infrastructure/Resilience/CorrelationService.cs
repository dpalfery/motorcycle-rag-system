using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MotorcycleRAG.Infrastructure.Resilience;

/// <summary>
/// Service for managing correlation IDs throughout the request lifecycle
/// </summary>
public class CorrelationService
{
    private readonly ILogger<CorrelationService> _logger;
    private static readonly AsyncLocal<string?> _correlationId = new();

    public CorrelationService(ILogger<CorrelationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the current correlation ID or generates a new one
    /// </summary>
    public string GetOrCreateCorrelationId()
    {
        var currentId = _correlationId.Value;
        if (!string.IsNullOrEmpty(currentId))
        {
            return currentId;
        }

        // Try to get from Activity (OpenTelemetry/Application Insights)
        var activity = Activity.Current;
        if (activity?.Id != null)
        {
            _correlationId.Value = activity.Id;
            return activity.Id;
        }

        // Generate new correlation ID
        var newId = GenerateCorrelationId();
        _correlationId.Value = newId;
        
        _logger.LogDebug("Generated new correlation ID: {CorrelationId}", newId);
        return newId;
    }

    /// <summary>
    /// Sets the correlation ID for the current context
    /// </summary>
    public void SetCorrelationId(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation ID cannot be null or empty", nameof(correlationId));
        }

        _correlationId.Value = correlationId;
        _logger.LogDebug("Set correlation ID: {CorrelationId}", correlationId);
    }

    /// <summary>
    /// Clears the current correlation ID
    /// </summary>
    public void ClearCorrelationId()
    {
        var currentId = _correlationId.Value;
        _correlationId.Value = null;
        
        if (!string.IsNullOrEmpty(currentId))
        {
            _logger.LogDebug("Cleared correlation ID: {CorrelationId}", currentId);
        }
    }

    /// <summary>
    /// Executes an operation with a specific correlation ID
    /// </summary>
    public async Task<T> ExecuteWithCorrelationAsync<T>(
        string correlationId,
        Func<Task<T>> operation)
    {
        var previousId = _correlationId.Value;
        SetCorrelationId(correlationId);

        try
        {
            return await operation();
        }
        finally
        {
            _correlationId.Value = previousId;
        }
    }

    /// <summary>
    /// Executes an operation with a specific correlation ID (no return value)
    /// </summary>
    public async Task ExecuteWithCorrelationAsync(
        string correlationId,
        Func<Task> operation)
    {
        await ExecuteWithCorrelationAsync(correlationId, async () =>
        {
            await operation();
            return true; // Dummy return value
        });
    }

    /// <summary>
    /// Creates a logging scope with the current correlation ID
    /// </summary>
    public IDisposable CreateLoggingScope()
    {
        var correlationId = GetOrCreateCorrelationId();
        return _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });
    }

    /// <summary>
    /// Creates a logging scope with additional properties
    /// </summary>
    public IDisposable CreateLoggingScope(Dictionary<string, object> additionalProperties)
    {
        var correlationId = GetOrCreateCorrelationId();
        var scopeProperties = new Dictionary<string, object>(additionalProperties)
        {
            ["CorrelationId"] = correlationId
        };

        return _logger.BeginScope(scopeProperties);
    }

    /// <summary>
    /// Generates a new correlation ID
    /// </summary>
    private string GenerateCorrelationId()
    {
        // Use a format similar to W3C Trace Context but simplified
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var random = Guid.NewGuid().ToString("N")[..12]; // Take first 12 chars
        return $"corr-{timestamp}-{random}";
    }
}

/// <summary>
/// Extension methods for ILogger to automatically include correlation ID
/// </summary>
public static class LoggerExtensions
{
    /// <summary>
    /// Logs an error with correlation context
    /// </summary>
    public static void LogErrorWithCorrelation<T>(
        this ILogger<T> logger,
        Exception exception,
        string message,
        string correlationId,
        params object[] args)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });
        
        logger.LogError(exception, message, args);
    }

    /// <summary>
    /// Logs a warning with correlation context
    /// </summary>
    public static void LogWarningWithCorrelation<T>(
        this ILogger<T> logger,
        string message,
        string correlationId,
        params object[] args)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });
        
        logger.LogWarning(message, args);
    }

    /// <summary>
    /// Logs information with correlation context
    /// </summary>
    public static void LogInformationWithCorrelation<T>(
        this ILogger<T> logger,
        string message,
        string correlationId,
        params object[] args)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });
        
        logger.LogInformation(message, args);
    }
}