namespace MotorcycleRAG.Contracts.Models.DTOs.Ingestion;

/// <summary>
/// Request body for POST /api/ingestion/artifacts/orphaned/{uploadId}/adopt.
/// Associates an orphaned search-chunks artifact with a valid ingestion job and re-indexes it.
/// </summary>
public sealed record OrphanAdoptRequest
{
    /// <summary>
    /// The ingestion job ID that the orphaned artifact should be associated with.
    /// Must be a valid, non-empty GUID that resolves to an existing ingestion job.
    /// </summary>
    public Guid IngestionJobId { get; init; }
}
