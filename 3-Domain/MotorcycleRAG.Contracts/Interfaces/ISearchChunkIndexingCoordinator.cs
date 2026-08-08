using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Coordinates search-chunk indexing with real anchors for an already-resolved ingestion job,
/// handling all aspects of indexing, persistence, and error recovery as a unified best-effort operation.
/// </summary>
public interface ISearchChunkIndexingCoordinator
{
    /// <summary>
    /// Indexes a search-chunks JSONL artifact for an already-resolved, valid ingestion job (non-null,
    /// <see cref="IngestionJob.IngestionJobId"/> != Guid.Empty) and persists the outcome. Never throws --
    /// every failure mode (indexing exception, job-transition exception, metadata-write exception) is caught
    /// internally and reported via the returned DTO, matching the best-effort semantics
    /// ProcessorArtifactService.ProcessSearchChunksAsync has today.
    /// </summary>
    /// <param name="jsonlStream">The search-chunks JSONL content. Caller owns positioning/seekability.</param>
    /// <param name="uploadId">The processor upload identifier that owns this artifact.</param>
    /// <param name="container">The blob container the artifact lives in (e.g. "search-chunks").</param>
    /// <param name="blobPath">The blob path of the artifact within <paramref name="container"/>.</param>
    /// <param name="indexedArtifactId">
    /// The pre-allocated, non-empty canonical artifact ID the caller has already generated for this indexing
    /// attempt (Guid.NewGuid() for a fresh upload/sweep-heal/adopt; never Guid.Empty).
    /// </param>
    /// <param name="job">
    /// The already-resolved, validated ingestion job (non-null, IngestionJobId != Guid.Empty). Callers perform
    /// job resolution and the D1 anchor-satisfiability guards themselves BEFORE calling this method -- the
    /// coordinator's contract begins only once a valid job exists.
    /// </param>
    /// <param name="cancellationToken">Propagates the caller's cancellation.</param>
    Task<SearchChunkIndexingOutcomeDto> IndexAsync(
        Stream jsonlStream,
        string uploadId,
        string container,
        string blobPath,
        Guid indexedArtifactId,
        IngestionJob job,
        CancellationToken cancellationToken = default);
}
