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
    /// Returns the most recent job for a given input reference and workflow type.
    /// </summary>
    Task<IngestionJob?> GetLatestByInputAsync(
        string inputRef,
        IngestionJobType inputType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the latest ingestion jobs for each unique (InputRef, InputType) combination.
    /// Used to batch-check pending storage files without N+1 queries.
    /// </summary>
    Task<IReadOnlyList<IngestionJob>> GetLatestByInputRefsAsync(
        IReadOnlyCollection<(string InputRef, IngestionJobType InputType)> pairs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent ingestion jobs across all inputs.
    /// </summary>
    Task<IReadOnlyList<IngestionJob>> GetRecentAsync(
        int maxCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all jobs for a given manual document ID (most recent first).
    /// </summary>
    Task<IReadOnlyList<IngestionJob>> GetByManualDocumentIdAsync(
        Guid manualDocumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all ingestion jobs matching any of the supplied statuses.
    /// </summary>
    Task<IReadOnlyList<IngestionJob>> GetByStatusesAsync(
        IReadOnlyCollection<IngestionJobStatus> statuses,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an ingestion job by identifier.
    /// </summary>
    Task<bool> DeleteAsync(
        Guid ingestionJobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all ingestion jobs for the supplied input reference.
    /// </summary>
    Task<int> DeleteByInputRefAsync(
        string inputRef,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all ingestion jobs matching any of the supplied statuses.
    /// </summary>
    Task<int> DeleteByStatusesAsync(
        IReadOnlyCollection<IngestionJobStatus> statuses,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the ingestion jobs with the supplied identifiers.
    /// </summary>
    Task<int> DeleteByIdsAsync(
        IReadOnlyCollection<Guid> ingestionJobIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically transitions a job to a terminal status if it is currently in the expected 'fromStatus'.
    /// Sets expected/indexed chunk counts and completion timestamp. Idempotent.
    /// </summary>
    /// <returns>true if the status transition was applied (WHERE matched); false if the job was not in fromStatus.</returns>
    Task<bool> TryTransitionToTerminalAsync(
        Guid ingestionJobId,
        IngestionJobStatus fromStatus,
        IngestionJobStatus toStatus,
        int? expectedChunkCount,
        int? indexedChunkCount,
        string? failureReason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically sets the job status to Deleting if the current status is in a deletable terminal state.
    /// Returns true if the update was applied, false if the job was not found or not in a deletable state.
    /// </summary>
    Task<bool> TrySetDeletingAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Updates the current stage, stage timestamp, and optional chunk counts for an active job.
    /// </summary>
    Task UpdateStageAsync(
        Guid ingestionJobId,
        string stage,
        int? chunksProcessed,
        int? totalChunks,
        string? failureReason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns an ingestion job by its document ingestion run ID, or null if not found.
    /// </summary>
    Task<IngestionJob?> GetByDocIngestionRunIdAsync(
        string docIngestionRunId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates only the <see cref="IngestionJob.MetadataJson"/> column for a job.
    /// Used to persist extracted or manually-submitted motorcycle metadata without
    /// touching other columns. Idempotent: repeated calls with the same value produce
    /// the same result.
    /// </summary>
    /// <param name="ingestionJobId">The job identifier.</param>
    /// <param name="metadataJson">The metadata JSON blob to persist (null clears the field).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateMetadataAsync(
        Guid ingestionJobId,
        string? metadataJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically transitions a job from <see cref="IngestionJobStatus.AwaitingMetadata"/> to
    /// <see cref="IngestionJobStatus.Processing"/>, setting the resume stage and clearing the
    /// failure reason. Uses a compare-and-swap guard (<c>WHERE [Status] = 'AwaitingMetadata'</c>)
    /// to prevent lost updates and TOCTOU races. Returns true if the transition was applied;
    /// false if the job was no longer in the AwaitingMetadata state (e.g. already resumed,
    /// cancelled, or deleted).
    /// </summary>
    /// <param name="ingestionJobId">The job identifier.</param>
    /// <param name="stage">The resume stage to set (e.g. <c>"resuming"</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the CAS-guarded transition matched (rows affected &gt; 0);
    /// <c>false</c> if the job was not in the AwaitingMetadata state.
    /// </returns>
    Task<bool> TryTransitionFromAwaitingMetadataAsync(
        Guid ingestionJobId,
        string stage,
        CancellationToken cancellationToken = default);
}
