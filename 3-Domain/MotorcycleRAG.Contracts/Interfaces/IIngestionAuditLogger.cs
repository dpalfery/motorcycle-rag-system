namespace MotorcycleRAG.Contracts.Interfaces;
/// <summary>
/// Audit logger for ingestion pipeline events.
/// Records who performed what action, when, and whether it succeeded —
/// without ever capturing raw file content, query text, or unnecessary PII.
/// </summary>
public interface IIngestionAuditLogger
{
    /// <summary>
    /// Records a named ingestion event with its outcome.
    /// </summary>
    /// <param name="eventName">Short, stable identifier for the event (e.g. "UploadReceived").</param>
    /// <param name="uploadId">Opaque identifier for the upload being processed.</param>
    /// <param name="userId">Subject (sub) claim of the acting user.</param>
    /// <param name="success">Whether the event completed successfully.</param>
    /// <param name="ct">Cancellation token.</param>
    Task LogAsync(string eventName, string uploadId, string userId, bool success, CancellationToken ct = default);

    /// <summary>
    /// Records a named ingestion failure event with a structured error code.
    /// </summary>
    /// <param name="eventName">Short, stable identifier for the event (e.g. "UploadValidationFailed").</param>
    /// <param name="uploadId">Opaque identifier for the upload being processed.</param>
    /// <param name="userId">Subject (sub) claim of the acting user.</param>
    /// <param name="errorCode">Structured, non-sensitive error code (e.g. "FILE_TOO_LARGE").</param>
    /// <param name="ct">Cancellation token.</param>
    Task LogErrorAsync(string eventName, string uploadId, string userId, string errorCode, CancellationToken ct = default);
}