using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Search service configuration settings bound to the "Search" section in configuration sources.
/// </summary>
public class SearchOptions
{
    private const int DefaultBatchSize = 100;
    private const int DefaultMaxSearchResults = 50;
    private const int MaxBatchSize = 1000;
    private const int MaxSearchResultsLimit = 100;

    private const int DefaultBatchIndexTimeoutSeconds = 30;

    [Required]
    public string IndexName { get; set; } = "motorcycle-sport";

    /// <summary>
    /// Hard upper bound on the wall-clock duration of a single
    /// <see cref="M:Azure.Search.Documents.SearchClient.MergeOrUploadDocumentsAsync"/> batch upload
    /// invoked by <see cref="T:MotorcycleRAG.Persistence.Azure.Search.ChunkIndexingService"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bounds the synchronous indexing call inside the upload controller so that a stalled
    /// Azure AI Search write cannot hang the HTTP request indefinitely (T7). On expiry the
    /// linked <see cref="System.Threading.CancellationToken"/> cancels the in-flight
    /// <see cref="Azure.Search.Documents.SearchClient"/> operation, the batch fails with a
    /// precise timeout reason, the controller propagates the failure, and the ingestion job
    /// transitions to <c>Failed</c> via the T6/T8 terminal-transition path.
    /// </para>
    /// <para>
    /// Default is 30 seconds per batch. Must be greater than zero and at most 600 seconds.
    /// </para>
    /// </remarks>
    [Range(1, 600)]
    public int BatchIndexTimeoutSeconds { get; set; } = DefaultBatchIndexTimeoutSeconds;

    /// <summary>
    /// Optional category wire-value ("dirt", "touring", "sport", "cruiser") that
    /// selects a single category-partitioned index for a query (D4). When null/empty
    /// or not a valid category, the query fans out across all four indexes and the
    /// results are merged by relevance score. Ignored by indexing (chunks carry their
    /// own <c>category</c> field).
    /// </summary>
    public string? Category { get; set; }

    [Range(1, MaxBatchSize)]
    public int BatchSize { get; set; } = DefaultBatchSize;

    [Range(1, MaxSearchResultsLimit)]
    public int MaxSearchResults { get; set; } = DefaultMaxSearchResults;

    public bool EnableHybridSearch { get; set; } = true;

    public bool EnableSemanticRanking { get; set; } = true;

    public int MaxResults { get; set; } = DefaultMaxSearchResults;

    public bool EnableCaching { get; set; } = true;

    public bool IncludeMetadata { get; set; } = true;

    public string ChunkIndexingProvider { get; set; } = "AzureSearch";

    public string? InMemoryShimEndpoint { get; set; }
}
