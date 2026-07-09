using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Application-layer service that orchestrates the ingestion job lifecycle:
/// creation, status retrieval, and cancellation.
/// </summary>
public interface IIngestionJobService {
    /// <summary>Lists source blobs that have not completed ingestion yet.</summary>
    Task<IReadOnlyList<PendingStorageFileDto>> GetPendingStorageFilesAsync(
        CancellationToken ct = default);

    /// <summary>Deletes a pending upload and any associated ingestion history.</summary>
    Task DeletePendingStorageFileAsync(
        string uploadId,
        string documentType,
        CancellationToken ct = default);

    /// <summary>Deletes every currently pending upload and its associated ingestion history.</summary>
    Task<int> ClearPendingStorageFilesAsync(
        CancellationToken ct = default);

    /// <summary>Lists recent ingestion jobs for status/history views.</summary>
    Task<IReadOnlyList<IngestionJobStatusResponse>> GetRecentIngestionJobsAsync(
        int maxCount = 50,
        CancellationToken ct = default);

    /// <summary>Cancels an active job if needed, then deletes the ingestion job and associated assets from history.</summary>
    Task DeleteJobAsync(
        Guid jobId,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Performs best-effort cleanup of artifacts, chunks, blobs, search documents,
    /// and graph data for a job previously marked as <see cref="IngestionJobStatus.Deleting"/>.
    /// Must be called by the background deletion service; not intended for HTTP-initiated calls.
    /// On success the job row is deleted. On failure the job is rolled back to
    /// <see cref="IngestionJobStatus.Failed"/> with the error reason recorded.
    /// </summary>
    Task ExecuteJobCleanupAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Deletes failed and cancelled ingestion jobs from history.</summary>
    Task<int> ClearFailedJobsAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>Deletes completed and partially completed ingestion jobs from history and storage.</summary>
    Task<int> ClearFinishedJobsAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>Retries a failed or cancelled ingestion job.</summary>
    Task<IngestionJobStatusResponse> RetryJobAsync(
        Guid jobId,
        string userId,
        CancellationToken ct = default);

    /// <summary>Creates a new ingestion job, persists it, and triggers the Fabric pipeline.</summary>
    Task<IngestionJobStatusResponse> StartJobAsync(
        IngestionJobStartRequest request,
        string userId,
        CancellationToken ct = default);

    /// <summary>Imports an already-processed graph artifact into the graph database and records job history.</summary>
    Task<IngestionJobStatusResponse> ImportGraphArtifactsAsync(
        GraphImportStartRequest request,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Executes graph ingestion for a single job dequeued from the graph ingestion channel.
    /// Called by the graph ingestion background service from a per-item DI scope. Ingestion
    /// failures are captured and persisted as a <see cref="IngestionJobStatus.Failed"/> status;
    /// this method does not throw for ordinary ingestion errors.
    /// </summary>
    /// <param name="job">The job to process; its <see cref="IngestionJob.InputRef"/> carries the upload id.</param>
    Task ProcessGraphIngestionJobAsync(IngestionJob job);

    /// <summary>Retrieves the current status of an ingestion job including coverage metrics.</summary>
    Task<IngestionJobStatusResponse?> GetJobStatusAsync(
        Guid jobId,
        string userId,
        CancellationToken ct = default);

    /// <summary>Cancels a running ingestion job.</summary>
    Task CancelJobAsync(
        Guid jobId,
        string userId,
        CancellationToken ct = default);

    /// <summary>Marks an active ingestion job as failed, such as when the local processor crashes.</summary>
    Task FailJobAsync(
        Guid jobId,
        string reason,
        string userId,
        CancellationToken ct = default);

    /// <summary>Updates the current pipeline stage and progress for an active job.</summary>
    Task<IngestionJobStatusResponse> TransitionStageAsync(
        Guid jobId,
        IngestionJobStageRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Submits manually-entered metadata for a job that is paused in the
    /// <see cref="IngestionJobStatus.AwaitingMetadata"/> state. Validates the JSON and required
    /// field completeness, persists it to <see cref="IngestionJob.MetadataJson"/>, transitions
    /// the job back to <see cref="IngestionJobStatus.Processing"/> via a CAS-guarded update,
    /// and signals the processor to resume. Handles duplicate submissions idempotently: a
    /// repeated call while the job is already past <see cref="IngestionJobStatus.AwaitingMetadata"/>
    /// updates the metadata but does not re-trigger the resume.
    /// </summary>
    /// <param name="jobId">The ingestion job identifier.</param>
    /// <param name="metadataJson">A JSON string with make, model, year, category, and optional tags.</param>
    /// <param name="userId">The Entra ID subject (sub claim) of the admin submitting the metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated job status response.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the job is not found.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="metadataJson"/> is not valid JSON, is not a JSON object, or is missing required fields.</exception>
    Task<IngestionJobStatusResponse> SubmitManualMetadataAsync(
        Guid jobId,
        string metadataJson,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves the current metadata (extracted or manually submitted) for an ingestion job.
    /// Returns an empty response with <c>IsComplete = false</c> when no metadata has been recorded.
    /// </summary>
    /// <param name="jobId">The ingestion job identifier.</param>
    /// <param name="userId">The Entra ID subject (sub claim) of the admin viewing the metadata (for audit logging).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The metadata response, or null when the job does not exist.</returns>
    Task<IngestionJobMetadataResponse?> GetJobMetadataAsync(
        Guid jobId,
        string userId,
        CancellationToken ct = default);
}
