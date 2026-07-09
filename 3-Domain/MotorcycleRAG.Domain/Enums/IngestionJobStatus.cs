namespace MotorcycleRAG.Domain.Enums;

/// <summary>
/// Status of an ingestion job
/// </summary>
public enum IngestionJobStatus
{
    Queued,
    Processing,
    Indexing,
    Completed,
    Failed,
    Cancelled,
    PartiallyCompleted,

    /// <remarks>
    /// Transient state indicating a background delete is in progress. Resolves to row
    /// deletion (job removed) or <see cref="Failed"/> if the delete cannot complete.
    /// </remarks>
    Deleting,

    /// <remarks>
    /// Paused state: the PDF pipeline's automated metadata extraction could not determine
    /// all required fields (make, model, year, category) after sampling the maximum number
    /// of pages. The pipeline is suspended until an admin submits manual metadata via
    /// <c>POST /api/ingestion/jobs/{jobId}/metadata</c>. On successful submission the job
    /// transitions back to <see cref="Processing"/> and the processor resumes from chunking.
    /// </remarks>
    AwaitingMetadata
}
