using MotorcycleRAG.Contracts.Models.DTOs;

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

    /// <summary>Deletes a queued or terminal ingestion job and associated assets from history.</summary>
    Task DeleteJobAsync(
        Guid jobId,
        string userId,
        CancellationToken ct = default);

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
}
