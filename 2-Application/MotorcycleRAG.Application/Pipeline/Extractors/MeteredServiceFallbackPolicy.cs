namespace MotorcycleRAG.Application.Pipeline.Extractors;

using Microsoft.Extensions.Logging;

/// <summary>
/// Executes a primary metered-service call and falls back to an alternative
/// when the primary throws a quota / rate-limit exception.
/// No Polly dependency — uses plain try/catch.
/// </summary>
public sealed class MeteredServiceFallbackPolicy {
    private readonly ILogger<MeteredServiceFallbackPolicy> _logger;

    public MeteredServiceFallbackPolicy(ILogger<MeteredServiceFallbackPolicy> logger) {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Attempts to execute <paramref name="primary"/>. If it throws an exception
    /// that indicates a quota or rate-limit error, logs a structured warning
    /// (no PII) and invokes <paramref name="fallback"/> instead.
    /// </summary>
    public async Task<T> ExecuteWithFallbackAsync<T>(
        Func<CancellationToken, Task<T>> primary,
        Func<CancellationToken, Task<T>> fallback,
        CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);

        try {
            return await primary(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsQuotaOrRateLimitError(ex)) {
            _logger.LogWarning(
                ex,
                "Metered service returned a quota/rate-limit error (ExceptionType={ExceptionType}, StatusCode={StatusCode}). Falling back to secondary provider.",
                ex.GetType().Name,
                ExtractStatusCode(ex));

            return await fallback(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Determines whether the exception signals a quota or rate-limit condition.
    /// Checks HTTP 429 (Too Many Requests) and 503 (Service Unavailable),
    /// as well as common quota-exceeded message patterns.
    /// </summary>
    private static bool IsQuotaOrRateLimitError(Exception ex) {
        // Check HTTP status code if available via Azure SDK or HttpRequestException.
        int? statusCode = ExtractStatusCode(ex);

        if (statusCode is 429 or 503) {
            return true;
        }

        // Fall back to message-based heuristic for non-HTTP exceptions.
        string message = ex.Message;

        return message.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("throttl", StringComparison.OrdinalIgnoreCase)
            || message.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
            || message.Contains("429", StringComparison.Ordinal);
    }

    /// <summary>
    /// Attempts to extract an HTTP status code from common exception types
    /// without taking a direct dependency on Azure SDK or System.Net.Http.
    /// </summary>
    private static int? ExtractStatusCode(Exception ex) {
        // System.Net.Http.HttpRequestException (.NET 10+ exposes StatusCode).
        if (ex is HttpRequestException httpEx) {
            return (int?)httpEx.StatusCode;
        }

        // Azure.RequestFailedException and similar — use reflection to avoid hard SDK dependency.
        var statusProp = ex.GetType().GetProperty("Status")
                      ?? ex.GetType().GetProperty("StatusCode");

        if (statusProp is not null && statusProp.GetValue(ex) is int code) {
            return code;
        }

        return null;
    }
}
