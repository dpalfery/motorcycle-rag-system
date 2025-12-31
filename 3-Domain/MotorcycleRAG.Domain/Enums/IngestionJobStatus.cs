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
    PartiallyCompleted
}
