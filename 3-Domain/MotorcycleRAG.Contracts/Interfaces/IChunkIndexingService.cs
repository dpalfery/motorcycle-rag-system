namespace MotorcycleRAG.Contracts.Interfaces;

using MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Indexes pre-embedded chunk records from a JSONL stream directly into Azure AI Search.
/// Called after the artifact is stored to blob to complete the ingestion pipeline.
/// </summary>
public interface IChunkIndexingService
{
    /// <summary>
    /// Reads <paramref name="jsonlStream"/> line-by-line and upserts each chunk into Azure AI Search.
    /// Returns detailed result with total parsed, succeeded, and failed chunk information.
    /// </summary>
    Task<ChunkIndexingResult> IndexFromJsonlAsync(Stream jsonlStream, string uploadId, CancellationToken ct = default);
}
