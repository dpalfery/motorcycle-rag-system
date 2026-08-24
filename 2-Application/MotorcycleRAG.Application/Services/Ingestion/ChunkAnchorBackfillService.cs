using System.Text.Json.Serialization;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Partial-update (merge-patch) document for an <see cref="IndexedChunkDto"/>.
/// Carries ONLY the four anchor fields required to backfill an existing Azure AI
/// Search chunk document: the document key (<see cref="Id"/>) plus the three
/// anchor fields (<see cref="IndexedArtifactId"/>, <see cref="IngestionJobId"/>,
/// <see cref="SourceContentHash"/>). Every other chunk field is intentionally
/// absent so that <c>MergeOrUploadDocumentsAsync</c> leaves existing vector,
/// content, and metadata values untouched — a merge-patch, not a full re-index.
/// </summary>
/// <remarks>
/// <para>
/// The wire shape (JSON key names + null-omission) is a property of the TYPE, not
/// the caller: every property carries <c>[JsonPropertyName]</c> matching the
/// Azure AI Search index schema (mirrors <c>ChunkIndexRecord</c>), and every
/// nullable anchor carries <c>[JsonIgnore(WhenWritingNull)]</c>. This means the
/// type serializes correctly under <b>default</b> <see cref="System.Text.Json.JsonSerializerOptions"/>
/// — the condition any operator harness uses — so a null <see cref="SourceContentHash"/>
/// is OMITTED from the patch (Azure AI Search leaves the existing value untouched)
/// rather than emitted as an explicit <c>"sourceContentHash": null</c> key, which
/// <c>mergeOrUpload</c> treats as "clear this field" (the silent §7 null-clobber
/// defect this type exists to prevent).
/// </para>
/// </remarks>
public record ChunkAnchorUpdateDocument(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("indexedArtifactId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IndexedArtifactId,
    [property: JsonPropertyName("ingestionJobId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IngestionJobId,
    [property: JsonPropertyName("sourceContentHash")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceContentHash);

/// <summary>
/// Builds idempotent partial-update documents that backfill the vector-graph
/// anchor identifiers (<c>indexedArtifactId</c>, <c>ingestionJobId</c>,
/// <c>sourceContentHash</c>) onto existing Azure AI Search chunk documents.
/// </summary>
/// <remarks>
/// <para>
/// This service is <b>construct-only</b>: its single public method
/// <see cref="BuildBackfillDocumentsAsync"/> returns the merge-patch documents
/// but performs no Azure AI Search write. Execution against the search index is
/// an operator/CI action that may run only after the T9 schema change is
/// deployed; this class is intentionally not wired into any CLI, controller
/// endpoint, hosted service, scheduled job, or DI-hosted pipeline that could
/// auto-trigger it.
/// </para>
/// <para>
/// The anchor values are derived from existing persisted state: the chunk's own
/// <see cref="IndexedChunkDto"/> row supplies <c>indexedArtifactId</c> and
/// <c>ingestionJobId</c>, and the owning <see cref="ManualDocument"/> supplies
/// <c>sourceContentHash</c>. No live Azure connection is opened — both inputs
/// are read through their respective repositories, which are mocked in unit
/// tests.
/// </para>
/// </remarks>
public sealed class ChunkAnchorBackfillService
{
    private readonly IIndexedChunkRepository _chunkRepository;
    private readonly IManualDocumentRepository _manualDocumentRepository;

    /// <summary>
    /// Initializes the service with its two read-only repository dependencies.
    /// </summary>
    /// <param name="chunkRepository">Reads existing <see cref="IndexedChunkDto"/> rows.</param>
    /// <param name="manualDocumentRepository">Reads the owning <see cref="ManualDocument"/> for its content hash.</param>
    public ChunkAnchorBackfillService(
        IIndexedChunkRepository chunkRepository,
        IManualDocumentRepository manualDocumentRepository)
    {
        _chunkRepository = chunkRepository ?? throw new ArgumentNullException(nameof(chunkRepository));
        _manualDocumentRepository = manualDocumentRepository ?? throw new ArgumentNullException(nameof(manualDocumentRepository));
    }

    /// <summary>
    /// Builds one partial-update (merge-patch) document per existing chunk for
    /// the supplied artifact, carrying only the four anchor fields.
    /// </summary>
    /// <param name="artifactId">The indexed artifact whose chunks require anchor backfill.</param>
    /// <param name="documentId">
    /// The <see cref="ManualDocument"/> identifier used to resolve
    /// <see cref="ManualDocument.SourceContentHash"/>. The document is looked up
    /// even when the artifact has no chunks, so callers should pass the owning
    /// document id consistently.
    /// </param>
    /// <param name="cancellationToken">Forwarded to both repository calls.</param>
    /// <returns>
    /// A read-only list of <see cref="ChunkAnchorUpdateDocument"/>, one per
    /// chunk; an empty list when the artifact has no chunks. Each document's
    /// <see cref="ChunkAnchorUpdateDocument.SourceContentHash"/> is
    /// <see langword="null"/> when the manual document is missing or its hash is
    /// null/whitespace, so the serialized merge-patch omits the key (via
    /// <c>JsonIgnoreCondition.WhenWritingNull</c>) rather than clobbering any
    /// existing value.
    /// </returns>
    public async Task<IReadOnlyList<ChunkAnchorUpdateDocument>> BuildBackfillDocumentsAsync(
        Guid artifactId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        // Both repositories are always consulted — even when the artifact has no
        // chunks — so the cancellation token propagates to each read and the
        // caller's idempotent operator script can rely on both lookups having
        // occurred for observability.
        var chunks = await _chunkRepository
            .GetByArtifactIdAsync(artifactId, cancellationToken)
            .ConfigureAwait(false);

        var manualDocument = await _manualDocumentRepository
            .GetDocumentByIdAsync(documentId, cancellationToken)
            .ConfigureAwait(false);

        // Resolve the source content hash exactly once. Null, empty, or
        // whitespace hashes are normalized to null so the merge-patch omits the
        // key rather than overwriting an existing hash with blank text.
        var sourceContentHash = NormalizeHash(manualDocument?.SourceContentHash);

        var artifactIdString = artifactId.ToString();

        // No chunks -> empty result. Bail before allocating the per-chunk
        // closures below. Both repository calls above have already executed, so
        // cancellation-token forwarding is still observable to tests.
        if (chunks.Count == 0)
        {
            return Array.Empty<ChunkAnchorUpdateDocument>();
        }

        var documents = new List<ChunkAnchorUpdateDocument>(chunks.Count);
        foreach (var chunk in chunks)
        {
            // The Azure AI Search document key is the chunk's ChunkId. It is
            // preserved verbatim (no casing normalization) — the index key must
            // match exactly what was originally indexed.
            // ingestionJobId comes from the chunk row itself, not the document,
            // so a chunk reprocessed under a different job carries its own job
            // anchor rather than the document's last run.
            documents.Add(new ChunkAnchorUpdateDocument(
                Id: chunk.ChunkId,
                IndexedArtifactId: artifactIdString,
                IngestionJobId: chunk.IngestionJobId.ToString(),
                SourceContentHash: sourceContentHash));
        }

        return documents;
    }

    private static string? NormalizeHash(string? hash) =>
        string.IsNullOrWhiteSpace(hash) ? null : hash;
}
