using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
namespace MotorcycleRAG.Application.Pipeline.Audit;

/// <summary>
/// Structured-logging implementation of <see cref="IIngestionAuditLogger"/>.
/// All parameters are sanitized (newlines replaced with spaces, truncated to 200 chars)
/// before being written to the log. No raw file content, query text, or PII
/// beyond uploadId/userId is ever logged.
/// </summary>
public sealed class IngestionAuditLogger : IIngestionAuditLogger
{
    private const int MaxParamLength = 200;

    private readonly ILogger<IngestionAuditLogger> _logger;
    public IngestionAuditLogger(ILogger<IngestionAuditLogger> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task LogAsync(
        string eventName,
        string uploadId,
        string userId,
        bool success,
        CancellationToken ct = default)
    {
        var safeEvent    = Sanitize(eventName);
        var safeUploadId = Sanitize(uploadId);
        var safeUserId   = Sanitize(userId);

        _logger.LogInformation(
            "Ingestion audit: event={Event} uploadId={UploadId} userId={UserId} success={Success}",
            safeEvent,
            safeUploadId,
            safeUserId,
            success);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task LogErrorAsync(
        string eventName,
        string uploadId,
        string userId,
        string errorCode,
        CancellationToken ct = default)
    {
        var safeEvent     = Sanitize(eventName);
        var safeUploadId  = Sanitize(uploadId);
        var safeUserId    = Sanitize(userId);
        var safeErrorCode = Sanitize(errorCode);
        _logger.LogWarning(
            "Ingestion audit error: event={Event} uploadId={UploadId} userId={UserId} errorCode={ErrorCode}",
            safeEvent,
            safeUploadId,
            safeUserId,
            safeErrorCode);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Replaces newline characters with a space and truncates to <see cref="MaxParamLength"/> chars.
    /// Returns an empty string when the input is null.
    /// </summary>
    private static string Sanitize(string? value)
    {
        if (value is null)
            return string.Empty;

        var sanitized = value
            .Replace('\n', ' ')
            .Replace('\r', ' ');

        return sanitized.Length > MaxParamLength
            ? sanitized[..MaxParamLength]
            : sanitized;
    }
}