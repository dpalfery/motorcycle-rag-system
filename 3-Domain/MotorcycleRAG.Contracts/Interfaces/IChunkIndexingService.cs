namespace MotorcycleRAG.Contracts.Interfaces;

using MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Indexes pre-embedded chunk records from a JSONL stream directly into Azure AI Search.
/// Called after the artifact is stored to blob to complete the ingestion pipeline.
/// </summary>
public interface IChunkIndexingService
{
    /// <summary>
    /// Reads <paramref name="jsonlStream"/> line-by-line, stamps each chunk with the provided anchor metadata,
    /// and upserts into Azure AI Search. Returns detailed result with total parsed, succeeded, and failed chunk information.
    /// </summary>
    /// <remarks>
    /// Per plan decision D3 (T14 contract closure), this is the SOLE <c>IndexFromJsonlAsync</c> overload.
    /// The caller (ProcessorArtifactService / ChunkReprocessService) resolves the canonical artifact ID,
    /// ingestion job ID, and source content hash BEFORE calling this method, and passes all three so every
    /// chunk carries its anchors on the single write — no two-phase merge-patch. The previous transitional
    /// 3-parameter overload was removed because it passed <c>Guid.Empty</c>/<c>string.Empty</c> anchors that
    /// Azure AI Search <c>mergeOrUpload</c> treated as "clear this field", silently wiping real anchors off
    /// already-indexed documents (plan §7 null-clobber footgun).
    /// </remarks>
    /// <param name="jsonlStream">A stream of JSONL-formatted chunk records.</param>
    /// <param name="uploadId">Unique identifier for the upload batch.</param>
    /// <param name="indexedArtifactId">The canonical artifact ID linking chunks to their indexed artifact in the relational store.</param>
    /// <param name="ingestionJobId">The ID of the ingestion job that produced these chunks.</param>
    /// <param name="sourceContentHash">The content hash or version tag for the source document. Null for jobs without a source document (e.g., StructuredSpecification or Batch).</param>
    /// <param name="ct">A cancellation token.</param>
    Task<ChunkIndexingResult> IndexFromJsonlAsync(
        Stream jsonlStream,
        string uploadId,
        Guid indexedArtifactId,
        Guid ingestionJobId,
        string? sourceContentHash,
        CancellationToken ct = default);
}
