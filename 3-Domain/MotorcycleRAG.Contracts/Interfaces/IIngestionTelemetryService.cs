namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Ingestion-pipeline-specific telemetry abstraction.
/// Tracks pipeline lifecycle events (started / completed / failed) via structured logging.
/// Implementations must never log file names, paths, query text, or PII.
/// </summary>
public interface IIngestionTelemetryService
{
    /// <summary>
    /// Records that an ingestion job has started.
    /// </summary>
    /// <param name="jobId">Opaque job identifier.</param>
    /// <param name="documentType">Document type hint (e.g. "manual-pdf").</param>
    /// <param name="userId">Sanitised user identifier.</param>
    void TrackIngestionStarted(Guid jobId, string documentType, string userId);

    /// <summary>
    /// Records that an ingestion job completed successfully.
    /// </summary>
    /// <param name="jobId">Opaque job identifier.</param>
    /// <param name="documentType">Document type hint.</param>
    /// <param name="duration">Wall-clock duration of the job.</param>
    /// <param name="itemsProcessed">Number of items (pages, rows, chunks) processed.</param>
    void TrackIngestionCompleted(Guid jobId, string documentType, TimeSpan duration, int itemsProcessed);

    /// <summary>
    /// Records that an ingestion job failed.
    /// </summary>
    /// <param name="jobId">Opaque job identifier.</param>
    /// <param name="documentType">Document type hint.</param>
    /// <param name="error">Exception that caused the failure (message only — no PII).</param>
    /// <param name="duration">Wall-clock duration until failure.</param>
    void TrackIngestionFailed(Guid jobId, string documentType, Exception error, TimeSpan duration);
}