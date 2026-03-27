using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Pipeline;

/// <summary>
/// Application-layer service that orchestrates the ingestion job lifecycle:
/// creation, status retrieval, and cancellation.
/// </summary>
public interface IIngestionJobService {
    /// <summary>Lists source blobs that have not completed ingestion yet.</summary>
    Task<IReadOnlyList<PendingStorageFileDto>> GetPendingStorageFilesAsync(
        CancellationToken ct = default);

    /// <summary>Lists recent ingestion jobs for status/history views.</summary>
    Task<IReadOnlyList<IngestionJobStatusResponse>> GetRecentIngestionJobsAsync(
        int maxCount = 50,
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
}
