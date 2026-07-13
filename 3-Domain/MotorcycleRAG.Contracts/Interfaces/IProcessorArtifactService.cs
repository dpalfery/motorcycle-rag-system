using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Coordinates local-processor artifact storage, source retrieval, and ingestion-stage reporting.
/// </summary>
public interface IProcessorArtifactService
{
    Task<ProcessorArtifactSourceResult> DownloadSourceAsync(
        string uploadId,
        string documentType,
        string? accessToken,
        CancellationToken cancellationToken = default);

    Task<ProcessorArtifactUploadResult> UploadArtifactAsync(
        ProcessorArtifactUploadRequest request,
        CancellationToken cancellationToken = default);

    Task<IngestionJobStatusResponse?> ReportJobStageByRunIdAsync(
        string runId,
        IngestionJobStageRequest request,
        CancellationToken cancellationToken = default);

    Task<IngestionJobStatusResponse> ReportJobStageAsync(
        Guid jobId,
        IngestionJobStageRequest request,
        CancellationToken cancellationToken = default);
}
