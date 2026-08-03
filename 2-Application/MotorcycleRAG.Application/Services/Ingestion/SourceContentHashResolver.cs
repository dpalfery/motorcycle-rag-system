using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Resolves the <see cref="ManualDocument.SourceContentHash"/> for an <see cref="IngestionJob"/>
/// from its owning <see cref="ManualDocument"/>, when one is linked.
/// </summary>
/// <remarks>
/// Shared by <see cref="ProcessorArtifactService"/> (initial ingestion, plan T8) and
/// <see cref="ChunkReprocessService"/> (reprocess, plan T13) so the null-vs-empty semantics are
/// identical across both paths -- this duplication is what previously allowed them to drift.
/// <para>
/// Returns <see langword="null"/> when the job has no associated <see cref="IngestionJob.ManualDocumentId"/>
/// or when no <see cref="ManualDocument"/> row is found. When a document is found, its
/// <see cref="ManualDocument.SourceContentHash"/> is returned verbatim (which may itself be
/// <see langword="null"/>). The helper NEVER coerces a null value to <see cref="string.Empty"/>:
/// an empty string would serialize as a real key and Azure AI Search <c>mergeOrUpload</c> would
/// treat it as "clear this field", silently wiping a real hash already indexed (the plan §7
/// null-clobber finding). A null/omitted key is what the <c>JsonIgnore(WhenWritingNull)</c> guard
/// downstream omits from the merge payload, leaving any existing value untouched.
/// </para>
/// </remarks>
internal static class SourceContentHashResolver
{
    /// <summary>
    /// Resolves the source content hash for the supplied job from its owning ManualDocument, if any.
    /// </summary>
    /// <param name="manualDocumentRepository">The repository used to look up the owning document.</param>
    /// <param name="job">The ingestion job whose anchors are about to be passed into indexing.</param>
    /// <param name="cancellationToken">Propagates the caller's cancellation.</param>
    /// <returns>
    /// The owning document's <see cref="ManualDocument.SourceContentHash"/> when one is linked and
    /// found; otherwise <see langword="null"/>. Never <see cref="string.Empty"/>.
    /// </returns>
    public static async Task<string?> ResolveAsync(
        IManualDocumentRepository manualDocumentRepository,
        IngestionJob job,
        CancellationToken cancellationToken)
    {
        if (!job.ManualDocumentId.HasValue)
        {
            return null;
        }

        var document = await manualDocumentRepository
            .GetDocumentByIdAsync(job.ManualDocumentId.Value, cancellationToken)
            .ConfigureAwait(false);
        return document?.SourceContentHash;
    }
}
