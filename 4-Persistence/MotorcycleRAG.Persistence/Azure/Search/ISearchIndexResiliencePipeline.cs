using Polly;

namespace MotorcycleRAG.Persistence.Azure.Search;

/// <summary>
/// Provides a Polly <see cref="ResiliencePipeline"/> that wraps the per-batch
/// <see cref="Azure.Search.Documents.SearchClient"/> upload operations issued by
/// <see cref="ChunkIndexingService"/>. The pipeline applies transient-only retry
/// (5xx / 429 / network) and <b>never</b> retries non-transient failures
/// (404 / 400 / 401 / 403). Per-batch timeout is enforced separately through a
/// linked <see cref="CancellationToken"/> passed into the upload call (T7).
/// </summary>
/// <remarks>
/// Lives in Persistence (not Contracts) because it is consumed entirely within the
/// Persistence layer alongside <see cref="ISearchClientFactory"/> and the concrete
/// <see cref="Azure.Search.Documents.SearchClient"/> it operates on. The Dependency Rule
/// forbids Contracts from referencing Polly or any infrastructure SDK.
/// </remarks>
public interface ISearchIndexResiliencePipeline
{
    /// <summary>
    /// The Polly resilience pipeline applied to <c>MergeOrUploadDocumentsAsync</c> batch
    /// uploads. Retries are limited to transient conditions; non-transient exceptions
    /// propagate immediately so the controller can transition the job to <c>Failed</c>.
    /// </summary>
    ResiliencePipeline Pipeline { get; }
}
