using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Search.Documents;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Exceptions;
using MotorcycleRAG.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using Polly;
using SearchOptions = MotorcycleRAG.Core.Options.SearchOptions;

namespace MotorcycleRAG.Persistence.Azure.Search;

/// <summary>
/// Indexes pre-embedded chunk records from a JSONL stream into the category-partitioned
/// Azure AI Search indexes (D4). Each chunk is routed to exactly one index based on its
/// category, and every batch uploaded to the Search service is homogeneous by category.
/// </summary>
/// <remarks>
/// <para>
/// <b>Category resolution (per chunk):</b>
/// <list type="number">
/// <item>If the chunk already carries a valid <c>category</c> wire-value (T2), use it &mdash;
/// this honors an operator-supplied <c>IngestionJobConfiguration.Category</c> override without
/// invoking the classifier (D7).</item>
/// <item>Otherwise resolve via <see cref="IMotorcycleCategoryClassifier"/> from (make, model),
/// caching the result per normalized (make, model) so each unique bike classifies at most once.</item>
/// <item>Otherwise fall back to the factory default (matches the classifier fallback).</item>
/// </list>
/// </para>
/// <para>
/// The <see cref="SearchClient"/> for each category index is resolved per batch through
/// <see cref="ISearchClientFactory"/>; no singleton client bound to one index remains.
/// </para>
/// </remarks>
public sealed class ChunkIndexingService : IChunkIndexingService
{
    private readonly ISearchClientFactory _clientFactory;
    private readonly IMotorcycleCategoryClassifier _categoryClassifier;
    private readonly ISearchIndexResiliencePipeline _resiliencePipeline;
    private readonly SearchOptions _searchOptions;
    private readonly ILogger<ChunkIndexingService> _logger;

    /// <summary>
    /// Batch size for bulk indexing operations (per category).
    /// </summary>
    private const int BatchSize = 100;

    public ChunkIndexingService(
        ISearchClientFactory clientFactory,
        IMotorcycleCategoryClassifier categoryClassifier,
        ISearchIndexResiliencePipeline resiliencePipeline,
        IOptions<SearchOptions> searchOptions,
        ILogger<ChunkIndexingService> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _categoryClassifier = categoryClassifier ?? throw new ArgumentNullException(nameof(categoryClassifier));
        _resiliencePipeline = resiliencePipeline ?? throw new ArgumentNullException(nameof(resiliencePipeline));
        _searchOptions = searchOptions?.Value ?? throw new ArgumentNullException(nameof(searchOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reads a JSONL stream line-by-line, resolves each chunk's category, groups chunks by
    /// category, and upserts each homogeneous group (in <see cref="BatchSize"/> sub-batches)
    /// into the matching category index.
    /// </summary>
    public async Task<ChunkIndexingResult> IndexFromJsonlAsync(Stream jsonlStream, string uploadId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(jsonlStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

        // Read + parse all records first, so category grouping can produce homogeneous batches.
        var records = new List<ChunkIndexRecord>();
        using var reader = new StreamReader(jsonlStream);
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var chunk = JsonSerializer.Deserialize<ChunkIndexRecord>(line);
                if (chunk is not null)
                {
                    records.Add(chunk);
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning(
                    jsonEx,
                    "Failed to parse chunk line for upload {UploadId}. Line will be skipped.",
                    uploadId);  // codeql[cs/log-forging]
            }
        }

        // Resolve + group by category (homogeneous groups drive per-index batches).
        var groups = await GroupByCategoryAsync(records, ct).ConfigureAwait(false);

        // T6 existence precheck: verify each target category index exists before sending ANY
        // batch. Per D1/D4 the API must NOT create indexes — they are provisioned by Pulumi
        // IaC. A missing index fails loudly here (SearchIndexNotFoundException) instead of
        // being silently swallowed as a per-chunk failure deep inside the batch loop.
        foreach (var category in groups.Keys)
        {
            var indexName = _clientFactory.GetIndexName(category);
            if (!await _clientFactory.IndexExistsAsync(category, ct).ConfigureAwait(false))
            {
                _logger.LogError(
                    "Search index {IndexName} does not exist for upload {UploadId}. Aborting before any batch is uploaded.",
                    indexName,
                    uploadId);  // codeql[cs/log-forging]
                throw new SearchIndexNotFoundException(indexName);
            }
        }

        var outcomes = new List<ChunkIndexOutcome>();
        var batchCount = 0;
        foreach (var (category, categoryRecords) in groups)
        {
            for (var i = 0; i < categoryRecords.Count; i += BatchSize)
            {
                var batch = categoryRecords.GetRange(i, Math.Min(BatchSize, categoryRecords.Count - i));
                var batchOutcomes = await IndexBatchForCategoryAsync(category, batch, uploadId, ct)
                    .ConfigureAwait(false);
                outcomes.AddRange(batchOutcomes);
                batchCount++;
            }
        }

        _logger.LogInformation(
            "Indexed {SuccessCount}/{TotalCount} chunks for upload {UploadId} across {CategoryCount} category indexes in {BatchCount} batches.",
            outcomes.Count(o => o.Succeeded),
            records.Count,
            uploadId,  // codeql[cs/log-forging]
            groups.Count,
            batchCount);

        return new ChunkIndexingResult(records.Count, batchCount, outcomes);
    }

    /// <summary>
    /// Resolves the category for every chunk and groups them into homogeneous, per-category
    /// batches. The resolution result is cached per normalized (make, model) so each unique
    /// bike triggers at most one classifier call.
    /// </summary>
    /// <remarks>
    /// Internal so the routing decision (the T4 acceptance criterion "Dirt chunks -&gt; Dirt index")
    /// is unit-testable in isolation, with the classifier mocked. No Azure SDK call happens here.
    /// </remarks>
    internal async Task<IReadOnlyDictionary<MotorcycleCategory, List<ChunkIndexRecord>>> GroupByCategoryAsync(
        IReadOnlyList<ChunkIndexRecord> chunks,
        CancellationToken ct)
    {
        var resolvedByBike = new Dictionary<string, MotorcycleCategory>(StringComparer.Ordinal);
        var groups = new Dictionary<MotorcycleCategory, List<ChunkIndexRecord>>();

        foreach (var chunk in chunks)
        {
            var category = await ResolveChunkCategoryAsync(chunk, resolvedByBike, ct).ConfigureAwait(false);

            if (!groups.TryGetValue(category, out var list))
            {
                list = new List<ChunkIndexRecord>();
                groups[category] = list;
            }

            list.Add(chunk);
        }

        return groups;
    }

    /// <summary>
    /// Resolves a single chunk's target category:
    /// (1) a valid embedded <c>category</c> field wins (operator override / upstream value),
    /// (2) else the classifier resolves (make, model), cached per bike,
    /// (3) else the factory default.
    /// </summary>
    internal async Task<MotorcycleCategory> ResolveChunkCategoryAsync(
        ChunkIndexRecord chunk,
        Dictionary<string, MotorcycleCategory> resolvedByBike,
        CancellationToken ct)
    {
        // (1) Prefer the embedded category — honors D7 operator override, avoids redundant LLM calls.
        if (MotorcycleCategory.TryParse(chunk.Category, out var embedded) && embedded.IsDefined)
        {
            return embedded;
        }

        // (2) Resolve via classifier, cached per normalized (make, model).
        if (!string.IsNullOrWhiteSpace(chunk.Make) || !string.IsNullOrWhiteSpace(chunk.Model))
        {
            var cacheKey = BuildBikeKey(chunk.Make, chunk.Model);
            if (!resolvedByBike.TryGetValue(cacheKey, out var resolved))
            {
                resolved = await _categoryClassifier
                    .ResolveCategoryAsync(chunk.Make ?? string.Empty, chunk.Model ?? string.Empty, ct)
                    .ConfigureAwait(false);
                resolvedByBike[cacheKey] = resolved.IsDefined ? resolved : _clientFactory.DefaultCategory;
            }

            return resolvedByBike[cacheKey];
        }

        // (3) No category signal at all — fall back to the factory default.
        _logger.LogWarning(
            "Chunk {ChunkId} has no category and no make/model; routing to default index {DefaultIndex}.",
            chunk.Id,
            _clientFactory.GetIndexName(_clientFactory.DefaultCategory));

        return _clientFactory.DefaultCategory;
    }

    /// <summary>
    /// Indexes a single homogeneous (same-category) batch into the matching category index.
    /// Emits structured per-batch observability logs (T10): one start line and one end summary
    /// carrying succeeded/failed counts, aggregated failure reasons, target index name, and
    /// duration, so indexing failures are diagnosable from logs rather than silent.
    /// </summary>
    private async Task<IReadOnlyList<ChunkIndexOutcome>> IndexBatchForCategoryAsync(
        MotorcycleCategory category,
        List<ChunkIndexRecord> batch,
        string uploadId,
        CancellationToken ct)
    {
        var outcomes = new List<ChunkIndexOutcome>();
        var indexName = _clientFactory.GetIndexName(category);
        var searchClient = _clientFactory.GetClient(category);
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Indexing batch start: {BatchSize} chunks targeting index {IndexName} (category={Category}) for upload {UploadId}.",
            batch.Count,
            indexName,
            category.Value,
            uploadId);  // codeql[cs/log-forging]

        // T7: bound the synchronous indexing call with a per-batch timeout linked to the
        // incoming token. The Azure SDK honours the cancellation token passed to
        // MergeOrUploadDocumentsAsync, so on expiry the in-flight write is cancelled and the
        // resulting OperationCanceledException propagates to the controller, which transitions
        // the ingestion job to Failed via the T6/T8 terminal-transition path. The default is
        // SearchOptions.BatchIndexTimeoutSeconds (30s); a value <= 0 means unbounded, in which
        // case only the external token applies.
        var batchTimeoutSeconds = _searchOptions.BatchIndexTimeoutSeconds;
        var batchTimeout = batchTimeoutSeconds > 0
            ? TimeSpan.FromSeconds(batchTimeoutSeconds)
            : Timeout.InfiniteTimeSpan;

        using var timeoutCts = new CancellationTokenSource(batchTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var linkedToken = linkedCts.Token;

        try
        {
            // T7: wrap the upload in a Polly resilience pipeline that retries only transient
            // failures (5xx / 429 / network). Non-transient status codes (404 / 400 / 401 /
            // 403) and SearchIndexNotFoundException propagate immediately so the controller
            // can transition the job to Failed with a precise reason.
            var result = await _resiliencePipeline.Pipeline.ExecuteAsync(
                async token =>
                {
                    linkedToken.ThrowIfCancellationRequested();
                    return await searchClient
                        .MergeOrUploadDocumentsAsync(batch, cancellationToken: token)
                        .ConfigureAwait(false);
                },
                linkedToken).ConfigureAwait(false);

            foreach (var indexResult in result.Value.Results)
            {
                var chunk = batch.FirstOrDefault(c => c.Id == indexResult.Key);
                if (chunk is not null)
                {
                    outcomes.Add(new ChunkIndexOutcome(
                        ChunkId: indexResult.Key,
                        Succeeded: indexResult.Succeeded,
                        FailureReason: indexResult.ErrorMessage,
                        PageNumber: chunk.PageNumber,
                        ChunkIndex: chunk.ChunkIndex,
                        SourceFile: chunk.SourceFile));
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // External cancellation (the caller's token fired, not the per-batch timeout).
            // Propagate as-is so the caller's own cancellation handling takes over; this is
            // NOT a timeout and must not be relabelled as one.
            RecordFailureForAllChunks(outcomes, batch, "Indexing batch cancelled by caller.");
            throw;
        }
        catch (OperationCanceledException)
        {
            // T7: the per-batch timeout fired (the linked token was cancelled and the caller's
            // token was not). Surface as a precise TimeoutException so the controller
            // transitions the job to Failed with a "timed out after {BatchIndexTimeoutSeconds}s"
            // reason rather than a generic cancellation. The only sources that cancel the linked
            // token are the caller's token (handled above) and this timeout, so any other
            // OperationCanceledException observed here is treated as a timeout failure.
            _logger.LogError(
                "Indexing batch of {BatchSize} chunks into category index {IndexName} for upload {UploadId} timed out after {TimeoutSeconds}s.",
                batch.Count,
                indexName,
                uploadId,  // codeql[cs/log-forging]
                batchTimeoutSeconds);

            var timeoutMessage = $"Indexing batch timed out after {batchTimeoutSeconds}s into index '{indexName}'.";
            RecordFailureForAllChunks(outcomes, batch, timeoutMessage);
            throw new TimeoutException(timeoutMessage);
        }
        catch (RequestFailedException rfe)
        {
            // Azure HTTP error. The resilience pipeline already retried transient 5xx/429 and
            // let non-transient 400/401/403/404 propagate immediately. Either way the whole
            // batch is dead — rethrow so the controller transitions the job to Failed (T6/T7
            // stop-silent-failure). Per-chunk outcomes are synthesized first so the T10 summary
            // (emitted in the finally below) still carries the failure reason.
            _logger.LogError(
                rfe,
                "Error indexing batch of {BatchSize} chunks into category index {IndexName} for upload {UploadId}. Status={Status}.",
                batch.Count,
                indexName,
                uploadId,  // codeql[cs/log-forging]
                rfe.Status);

            RecordFailureForAllChunks(outcomes, batch, rfe.Message);
            throw;
        }
        catch (Exception ex)
        {
            // Non-Azure-HTTP failure (e.g. network, serialization, unexpected SDK error). If it
            // is structurally non-transient (SearchIndexNotFoundException) rethrow loudly (T6);
            // otherwise record per-chunk outcomes and let the T10 summary report the failure so
            // the run is diagnosable rather than silent.
            _logger.LogError(
                ex,
                "Error indexing batch of {BatchSize} chunks into category index {IndexName} for upload {UploadId}.",
                batch.Count,
                indexName,
                uploadId);  // codeql[cs/log-forging]

            RecordFailureForAllChunks(outcomes, batch, ex.Message);

            if (IsNonTransient(ex))
            {
                throw;
            }
        }
        finally
        {
            stopwatch.Stop();

            var succeeded = outcomes.Count(o => o.Succeeded);
            var failed = outcomes.Count - succeeded;
            var failureReasons = AggregateFailureReasons(outcomes);

            // T10 per-batch structured summary: one line with counts + failure reasons + target
            // index. Emitted from finally so it is produced for both successful and failing
            // (rethrown) batches — indexing failures must be diagnosable from logs, not silent.
            _logger.LogInformation(
                "Indexed {Succeeded}/{Total} chunks to {IndexName} in {DurationMs}ms (category={Category}, failed={FailedCount}, failureReasons={FailureReasons}).",
                succeeded,
                batch.Count,
                indexName,
                stopwatch.ElapsedMilliseconds,
                category.Value,
                failed,
                failureReasons);
        }

        return outcomes;
    }

    /// <summary>
    /// Records a failed <see cref="ChunkIndexOutcome"/> for every chunk in <paramref name="batch"/>
    /// using a single shared <paramref name="failureReason"/>. Used by the failure branches of
    /// <see cref="IndexBatchForCategoryAsync"/> so the T10 per-batch summary (emitted in the
    /// finally) reports accurate succeeded/failed counts and the failure reason even when the
    /// entire batch upload threw before any per-document result was returned.
    /// </summary>
    private static void RecordFailureForAllChunks(
        List<ChunkIndexOutcome> outcomes,
        List<ChunkIndexRecord> batch,
        string failureReason)
    {
        foreach (var chunk in batch)
        {
            outcomes.Add(new ChunkIndexOutcome(
                ChunkId: chunk.Id,
                Succeeded: false,
                FailureReason: failureReason,
                PageNumber: chunk.PageNumber,
                ChunkIndex: chunk.ChunkIndex,
                SourceFile: chunk.SourceFile));
        }
    }

    /// <summary>
    /// Aggregates the distinct failure reasons from a batch's outcomes into a single
    /// structured-log-friendly string. Returns <see cref="string.Empty"/> when there are
    /// no failures so the summary field stays deterministic for downstream log queries (T10).
    /// </summary>
    private static string AggregateFailureReasons(IReadOnlyList<ChunkIndexOutcome> outcomes)
    {
        var distinctReasons = outcomes
            .Where(static o => !o.Succeeded && !string.IsNullOrWhiteSpace(o.FailureReason))
            .Select(static o => o.FailureReason)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return distinctReasons.Count == 0
            ? string.Empty
            : string.Join(" | ", distinctReasons);
    }

    /// <summary>
    /// Determines whether an indexing exception is non-transient, meaning the entire batch
    /// is dead and retrying would not help. Non-transient errors are rethrown (T6) and
    /// never retried (T7 transient-only Polly).
    /// </summary>
    /// <remarks>
    /// Non-transient = the batch is structurally wrong: the target index does not exist
    /// (<see cref="SearchIndexNotFoundException"/>), the request body is malformed (400),
    /// or authentication/authorization failed (401/403).
    /// </remarks>
    private static bool IsNonTransient(Exception ex)
    {
        if (ex is SearchIndexNotFoundException)
        {
            return true;
        }

        if (ex is RequestFailedException rfe)
        {
            return rfe.Status is 400 or 401 or 403 or 404;
        }

        return false;
    }

    private static string BuildBikeKey(string? make, string? model)
    {
        var m = (make ?? string.Empty).Trim();
        var mo = (model ?? string.Empty).Trim();
        return $"{m}|{mo}";
    }

    /// <summary>
    /// Represents a chunk record as stored in Azure AI Search.
    /// Field names via [JsonPropertyName] must match the Azure Search index schema.
    /// </summary>
    internal sealed record ChunkIndexRecord
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; init; } = string.Empty;

        [JsonPropertyName("documentType")]
        public string DocumentType { get; init; } = string.Empty;

        /// <summary>
        /// Motorcycle category ("dirt", "touring", "sport", "cruiser"). Mirrors the
        /// filterable+facetable <c>category</c> field in the Azure AI Search index
        /// schema and the <c>category</c> key emitted by the Python chunkers.
        /// String (not the typed value object) so the wire record matches the index
        /// schema exactly and deserializes from JSONL under default options.
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; init; } = string.Empty;

        [JsonPropertyName("make")]
        public string? Make { get; init; }

        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("year")]
        public int Year { get; init; }

        [JsonPropertyName("sourceFile")]
        public string? SourceFile { get; init; }

        [JsonPropertyName("section")]
        public string? Section { get; init; }

        [JsonPropertyName("pageNumber")]
        public int PageNumber { get; init; }

        [JsonPropertyName("pageRange")]
        public string? PageRange { get; init; }

        [JsonPropertyName("primarySection")]
        public string? PrimarySection { get; init; }

        [JsonPropertyName("sectionLevel")]
        public int SectionLevel { get; init; }

        [JsonPropertyName("sectionHeadings")]
        public List<string> SectionHeadings { get; init; } = [];

        [JsonPropertyName("tableCaption")]
        public string? TableCaption { get; init; }

        [JsonPropertyName("chunkIndex")]
        public int ChunkIndex { get; init; }

        [JsonPropertyName("tags")]
        public List<string> Tags { get; init; } = [];

        [JsonPropertyName("contentVector")]
        public float[] ContentVector { get; init; } = [];

        [JsonPropertyName("createdAt")]
        public DateTimeOffset CreatedAt { get; init; }

        [JsonPropertyName("updatedAt")]
        public DateTimeOffset UpdatedAt { get; init; }
    }
}
