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
    Deleting
}
