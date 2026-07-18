using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;

namespace MotorcycleRAG.Application.Services.Ingestion.Audit;

/// <summary>
/// Structured-logging implementation of <see cref="IIngestionAuditLogger"/>.
/// Log value sanitization (newline/control-char stripping, truncation) is applied
/// centrally by the registered <c>SanitizingLoggerProvider</c>, so values are
/// forwarded to the logger as-is. No raw file content, query text, or PII
/// beyond uploadId/userId is ever logged.
/// </summary>
public sealed class IngestionAuditLogger : IIngestionAuditLogger
{
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
        _logger.LogInformation(
            "Ingestion audit: event={Event} uploadId={UploadId} userId={UserId} success={Success}",
            eventName,
            uploadId,
            userId,
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
        _logger.LogWarning(
            "Ingestion audit error: event={Event} uploadId={UploadId} userId={UserId} errorCode={ErrorCode}",
            eventName,
            uploadId,
            userId,
            errorCode);

        return Task.CompletedTask;
    }

}