using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Repository abstraction for persisting and querying ingestion job records.
/// All methods are fully asynchronous; implementations must use parameterized SQL (no string concat).
/// </summary>
public interface IIngestionJobRepository
{
    /// <summary>Creates a new ingestion job record and returns the persisted entity.</summary>
    Task<IngestionJob> CreateAsync(IngestionJob job, CancellationToken cancellationToken = default);

    /// <summary>Returns an ingestion job by its GUID, or null if not found.</summary>
    Task<IngestionJob?> GetByIdAsync(Guid ingestionJobId, CancellationToken cancellationToken = default);

    /// <summary>Updates the status and optional fields of an existing job.</summary>
    Task UpdateAsync(IngestionJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates only the status and failure reason — a lightweight operation for status transitions.
    /// </summary>
    Task UpdateStatusAsync(
        Guid ingestionJobId,
        IngestionJobStatus status,
        string? failureReason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent job for a given input reference (uploadId/blobKey) for idempotency checks.
    /// </summary>
    Task<IngestionJob?> GetLatestByInputRefAsync(string inputRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all jobs for a given manual document ID (most recent first).
    /// </summary>
    Task<IReadOnlyList<IngestionJob>> GetByManualDocumentIdAsync(
        Guid manualDocumentId,
        CancellationToken cancellationToken = default);
}
