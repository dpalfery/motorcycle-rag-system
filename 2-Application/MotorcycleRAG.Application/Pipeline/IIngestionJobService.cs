using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Pipeline;

/// <summary>
/// Application-layer service that orchestrates the ingestion job lifecycle:
/// creation, status retrieval, and cancellation.
/// </summary>
public interface IIngestionJobService {
    /// <summary>Creates a new ingestion job, persists it, and triggers the Fabric pipeline.</summary>
    Task<IngestionJobStatusResponse> StartJobAsync(
        IngestionJobStartRequest request,
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