using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;

namespace MotorcycleRAG.Persistence.Telemetry;

/// <summary>
/// Structured-logging implementation of <see cref="IIngestionTelemetryService"/>.
/// Uses <see cref="ILogger"/> only — no dependency on Application Insights TelemetryClient.
/// File names, paths, query text, and PII are never logged.
/// </summary>
public sealed class IngestionTelemetryService : IIngestionTelemetryService
{
    private readonly ILogger<IngestionTelemetryService> _logger;

    public IngestionTelemetryService(ILogger<IngestionTelemetryService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void TrackIngestionStarted(Guid jobId, string documentType, string userId)
    {
        ArgumentNullException.ThrowIfNull(userId);
        var safeUserId = SanitiseUserId(userId);

        _logger.LogInformation(
            "Ingestion started. JobId={JobId}, DocumentType={DocumentType}, UserId={UserId}.",
            jobId,
            documentType,
            safeUserId);
    }

    /// <inheritdoc />
    public void TrackIngestionCompleted(Guid jobId, string documentType, TimeSpan duration, int itemsProcessed)
    {
        _logger.LogInformation(
            "Ingestion completed. JobId={JobId}, DocumentType={DocumentType}, DurationMs={DurationMs}, ItemsProcessed={ItemsProcessed}.",
            jobId,
            documentType,
            duration.TotalMilliseconds,
            itemsProcessed);
    }

    /// <inheritdoc />
    public void TrackIngestionFailed(Guid jobId, string documentType, Exception error, TimeSpan duration)
    {
        _logger.LogError(
            error,
            "Ingestion failed. JobId={JobId}, DocumentType={DocumentType}, DurationMs={DurationMs}.",
            jobId,
            documentType,
            duration.TotalMilliseconds);
    }

    /// <summary>
    /// Replaces newlines and tabs with spaces to prevent log-injection.
    /// </summary>
    private static string SanitiseUserId(string userId)
    {
        return userId
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace('\t', ' ');
    }
}