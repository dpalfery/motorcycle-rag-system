using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Represents a Microsoft Fabric pipeline ingestion job.
/// Tracks the full lifecycle of a single ingestion operation from upload to completion.
/// Supports FR-009 (trigger ingestion), FR-010 (status), FR-010a (coverage reporting).
/// </summary>
/// <remarks>
/// State is encapsulated: identity and coverage properties are immutable after construction,
/// and lifecycle transitions are enforced exclusively through the named behavior methods
/// (<see cref="StartProcessing"/>, <see cref="Complete"/>, <see cref="Fail"/>, etc.). Use
/// <see cref="Create"/> for new jobs and <see cref="Rehydrate"/> to rebuild a persisted job at
/// the Persistence boundary. There are no public or init setters.
/// </remarks>
public class IngestionJob
{
    // --- Backing fields for lifecycle state mutated only by behavior methods ---

    private IngestionJobStatus _status;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _completedAtUtc;
    private string? _failureReason;
    private string? _metadataJson;
    private string? _currentStage;
    private DateTimeOffset? _stageSetAtUtc;

    private IngestionJob(
        long id,
        Guid ingestionJobId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? completedAtUtc,
        string? createdBySubject,
        IngestionJobStatus status,
        string? failureReason,
        string? errorsJson,
        string? errorMessage,
        IngestionJobType inputType,
        string inputRef,
        string? sourceFileName,
        string computeProvider,
        string? docIngestionRunId,
        Guid? manualDocumentId,
        int? totalPages,
        int? pagesCapturedViewableCount,
        int? pagesWithSearchableTextCount,
        int? pagesWithOcrTextCount,
        int? pagesWithNativeTextCount,
        string? missingPagesJson,
        string? metricsJson,
        int? expectedChunkCount,
        int? indexedChunkCount,
        string? currentStage,
        DateTimeOffset? stageSetAtUtc,
        string? metadataJson)
    {
        Id = id;
        IngestionJobId = ingestionJobId;
        CreatedAtUtc = createdAtUtc;
        _startedAtUtc = startedAtUtc;
        _completedAtUtc = completedAtUtc;
        CreatedBySubject = createdBySubject;
        _status = status;
        _failureReason = failureReason;
        ErrorsJson = errorsJson;
        ErrorMessage = errorMessage;
        InputType = inputType;
        InputRef = inputRef;
        SourceFileName = sourceFileName;
        ComputeProvider = computeProvider;
        DocIngestionRunId = docIngestionRunId;
        ManualDocumentId = manualDocumentId;
        TotalPages = totalPages;
        PagesCapturedViewableCount = pagesCapturedViewableCount;
        PagesWithSearchableTextCount = pagesWithSearchableTextCount;
        PagesWithOcrTextCount = pagesWithOcrTextCount;
        PagesWithNativeTextCount = pagesWithNativeTextCount;
        MissingPagesJson = missingPagesJson;
        MetricsJson = metricsJson;
        ExpectedChunkCount = expectedChunkCount;
        IndexedChunkCount = indexedChunkCount;
        _currentStage = currentStage;
        _stageSetAtUtc = stageSetAtUtc;
        _metadataJson = metadataJson;
    }

    // --- Identity (immutable) ---

    /// <summary>SQL identity key for display and sorting.</summary>
    public long Id { get; }

    /// <summary>Primary key — GUID assigned at creation time.</summary>
    public Guid IngestionJobId { get; }

    /// <summary>UTC timestamp when the job record was created (upload accepted).</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>Entra ID subject (oid) of the user who initiated the job. Never logged raw.</summary>
    public string? CreatedBySubject { get; }

    /// <summary>Type of ingestion input (PDFManual, StructuredSpecification, etc.).</summary>
    public IngestionJobType InputType { get; }

    /// <summary>
    /// Opaque reference to the uploaded blob (blob key or upload GUID).
    /// Never a filesystem path. Format: manuals/{guid}/upload/{filename} or uploads/{guid}.
    /// </summary>
    public string InputRef { get; }

    /// <summary>Original file name of the uploaded artifact, when known at creation.</summary>
    public string? SourceFileName { get; }

    /// <summary>Compute provider used. Defaults to "MicrosoftFabric".</summary>
    // vestigial: Microsoft Fabric compute provider kept for backward compatibility; LocalProcessingService is preferred
    public string ComputeProvider { get; }

    /// <summary>Document ingestion run ID for status polling and audit. Null until pipeline is triggered. Vestigial reference to Fabric.</summary>
    public string? DocIngestionRunId { get; }

    /// <summary>
    /// GUID of the MotorcycleManual document produced by this job.
    /// Populated only when InputType is PDFManual and job reaches Completed/PartiallyCompleted.
    /// </summary>
    public Guid? ManualDocumentId { get; }

    // --- Coverage fields (populated by Fabric pipeline upon completion; immutable here) ---

    /// <summary>Total pages in the ingested PDF. Null for non-PDF jobs or until pipeline reports.</summary>
    public int? TotalPages { get; }

    /// <summary>Pages that were captured as viewable (rendered PNG). Null until pipeline reports.</summary>
    public int? PagesCapturedViewableCount { get; }

    /// <summary>Pages with searchable text (OCR or native). Null until pipeline reports.</summary>
    public int? PagesWithSearchableTextCount { get; }

    /// <summary>Pages processed via OCR. Null until pipeline reports.</summary>
    public int? PagesWithOcrTextCount { get; }

    /// <summary>Pages with native embedded text (no OCR needed). Null until pipeline reports.</summary>
    public int? PagesWithNativeTextCount { get; }

    /// <summary>
    /// JSON array of missing page numbers (pages that could not be rendered or extracted).
    /// Stored as JSON for SQL persistence; deserialize before use.
    /// </summary>
    public string? MissingPagesJson { get; }

    /// <summary>
    /// JSON object with extended pipeline metrics (timing, token counts, etc.).
    /// Stored as JSON for SQL persistence; deserialize before use.
    /// </summary>
    public string? MetricsJson { get; }

    // --- Lifecycle state (read-only publicly; mutated only via behavior methods) ---

    /// <summary>UTC timestamp when the Fabric pipeline run actually started. Null until pipeline starts.</summary>
    public DateTimeOffset? StartedAtUtc => _startedAtUtc;

    /// <summary>UTC timestamp when the job reached a terminal state (Completed, Failed, Cancelled).</summary>
    public DateTimeOffset? CompletedAtUtc => _completedAtUtc;

    /// <summary>Current job lifecycle status.</summary>
    public IngestionJobStatus Status => _status;

    /// <summary>Human-readable failure description. Populated only on terminal failure.</summary>
    public string? FailureReason => _failureReason;

    /// <summary>
    /// Motorcycle metadata extracted by the PDF pipeline's LLM or submitted manually by an
    /// admin. Stored as a JSON blob with the shape:
    /// <c>{ "make": "...", "model": "...", "year": 2023, "category": "...", "tags": [...] }</c>.
    /// Populated during the <c>extracting-metadata</c> pipeline stage. When automated extraction
    /// is incomplete the job pauses in <see cref="IngestionJobStatus.AwaitingMetadata"/>
    /// and this field holds the partial result; once an admin submits manual metadata it is
    /// overwritten with the complete blob. Column is <c>nvarchar(max)</c>.
    /// </summary>
    public string? MetadataJson => _metadataJson;

    /// <summary>
    /// Current pipeline stage reported by the local processor.
    /// Values: copying, parsing, chunking, embedding, uploading-chunks, extracting-graph, uploading-graph, completed.
    /// </summary>
    public string? CurrentStage => _currentStage;

    /// <summary>UTC timestamp when the current stage was last updated.</summary>
    public DateTimeOffset? StageSetAtUtc => _stageSetAtUtc;

    /// <summary>Expected number of chunks parsed from the JSONL artifact. Null until indexing is attempted.</summary>
    public int? ExpectedChunkCount { get; private set; }

    /// <summary>Number of chunks successfully indexed into Azure Search. Null until indexing is attempted.</summary>
    public int? IndexedChunkCount { get; private set; }

    /// <summary>Structured failure detail (JSON) recorded by <see cref="RecordFailure"/>.</summary>
    public string? ErrorsJson { get; private set; }

    /// <summary>Single-line summary of the last failure, derived from <see cref="ErrorsJson"/>.</summary>
    public string? ErrorMessage { get; private set; }

    // --- Factories ---

    /// <summary>
    /// Creates a new ingestion job with a fresh identifier and default state. Identity fields
    /// are captured here; lifecycle transitions happen via the behavior methods.
    /// </summary>
    /// <param name="inputType">Type of ingestion input.</param>
    /// <param name="inputRef">Opaque reference to the uploaded blob.</param>
    /// <param name="createdBySubject">Entra ID subject (oid) of the originating user.</param>
    /// <param name="sourceFileName">Optional original file name.</param>
    /// <param name="computeProvider">Compute provider; defaults to "MicrosoftFabric".</param>
    /// <param name="docIngestionRunId">Optional document ingestion run id for status polling.</param>
    /// <param name="initialStatus">Initial lifecycle status; defaults to <see cref="IngestionJobStatus.Queued"/>.</param>
    /// <param name="startedAtUtc">Optional start timestamp for jobs that begin processing immediately.</param>
    public static IngestionJob Create(
        IngestionJobType inputType,
        string inputRef,
        string? createdBySubject,
        string? sourceFileName = null,
        string computeProvider = "MicrosoftFabric",
        string? docIngestionRunId = null,
        IngestionJobStatus initialStatus = IngestionJobStatus.Queued,
        DateTimeOffset? startedAtUtc = null) =>
        Rehydrate(
            id: 0,
            ingestionJobId: Guid.NewGuid(),
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: startedAtUtc,
            completedAtUtc: null,
            createdBySubject: createdBySubject,
            status: initialStatus,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: inputType,
            inputRef: inputRef,
            sourceFileName: sourceFileName,
            computeProvider: computeProvider,
            docIngestionRunId: docIngestionRunId,
            manualDocumentId: null,
            totalPages: null,
            pagesCapturedViewableCount: null,
            pagesWithSearchableTextCount: null,
            pagesWithOcrTextCount: null,
            pagesWithNativeTextCount: null,
            missingPagesJson: null,
            metricsJson: null,
            expectedChunkCount: null,
            indexedChunkCount: null,
            currentStage: null,
            stageSetAtUtc: null,
            metadataJson: null);

    /// <summary>
    /// Rehydrates a persisted job after validating the complete lifecycle state at the
    /// Persistence boundary. Invalid database rows are rejected rather than becoming a
    /// partially-valid domain entity.
    /// </summary>
    public static IngestionJob Rehydrate(
        long id,
        Guid ingestionJobId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? completedAtUtc,
        string? createdBySubject,
        IngestionJobStatus status,
        string? failureReason,
        string? errorsJson,
        string? errorMessage,
        IngestionJobType inputType,
        string inputRef,
        string? sourceFileName,
        string computeProvider,
        string? docIngestionRunId,
        Guid? manualDocumentId,
        int? totalPages,
        int? pagesCapturedViewableCount,
        int? pagesWithSearchableTextCount,
        int? pagesWithOcrTextCount,
        int? pagesWithNativeTextCount,
        string? missingPagesJson,
        string? metricsJson,
        int? expectedChunkCount,
        int? indexedChunkCount,
        string? currentStage,
        DateTimeOffset? stageSetAtUtc,
        string? metadataJson)
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "Job ID cannot be negative.");
        }

        if (ingestionJobId == Guid.Empty)
        {
            throw new ArgumentException("Ingestion job id is required.", nameof(ingestionJobId));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown ingestion job status.");
        }

        if (!Enum.IsDefined(inputType))
        {
            throw new ArgumentOutOfRangeException(nameof(inputType), inputType, "Unknown ingestion job type.");
        }

        if (string.IsNullOrWhiteSpace(inputRef))
        {
            throw new ArgumentException("Input reference is required.", nameof(inputRef));
        }

        return new IngestionJob(
            id,
            ingestionJobId,
            createdAtUtc.ToUniversalTime(),
            startedAtUtc?.ToUniversalTime(),
            completedAtUtc?.ToUniversalTime(),
            createdBySubject,
            status,
            failureReason,
            errorsJson,
            errorMessage,
            inputType,
            inputRef,
            sourceFileName,
            string.IsNullOrWhiteSpace(computeProvider) ? "MicrosoftFabric" : computeProvider,
            docIngestionRunId,
            manualDocumentId,
            totalPages,
            pagesCapturedViewableCount,
            pagesWithSearchableTextCount,
            pagesWithOcrTextCount,
            pagesWithNativeTextCount,
            missingPagesJson,
            metricsJson,
            expectedChunkCount,
            indexedChunkCount,
            currentStage,
            stageSetAtUtc,
            metadataJson);
    }

    // --- Lifecycle behavior methods ---

    public void StartProcessing(DateTimeOffset? atUtc = null)
    {
        EnsureTransitionAllowed(IngestionJobStatus.Processing);
        _status = IngestionJobStatus.Processing;
        _startedAtUtc ??= atUtc ?? DateTimeOffset.UtcNow;
    }

    public void PauseForMetadata(string? reason, string stage, DateTimeOffset? atUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        EnsureTransitionAllowed(IngestionJobStatus.AwaitingMetadata);
        _status = IngestionJobStatus.AwaitingMetadata;
        _currentStage = stage;
        _stageSetAtUtc = atUtc ?? DateTimeOffset.UtcNow;
        _failureReason = string.IsNullOrWhiteSpace(reason)
            ? "Metadata extraction incomplete. Manual entry required."
            : reason;
    }

    public void ResumeFromMetadata(string stage, DateTimeOffset? atUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        if (_status != IngestionJobStatus.AwaitingMetadata)
        {
            throw new InvalidOperationException($"Only a job awaiting metadata can resume; current status is {_status}.");
        }

        _status = IngestionJobStatus.Processing;
        _currentStage = stage;
        _stageSetAtUtc = atUtc ?? DateTimeOffset.UtcNow;
        _failureReason = null;
    }

    public void SetMetadata(string? metadataJson) => _metadataJson = metadataJson;

    public void UpdateStage(string stage, int? chunksProcessed, int? totalChunks, DateTimeOffset? atUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        if (_status == IngestionJobStatus.Queued)
        {
            StartProcessing(atUtc);
        }

        if (IsTerminal(_status))
        {
            throw new InvalidOperationException($"A job in {_status} state cannot update its stage.");
        }

        _currentStage = stage;
        _stageSetAtUtc = atUtc ?? DateTimeOffset.UtcNow;
        ExpectedChunkCount ??= totalChunks;
        IndexedChunkCount = chunksProcessed;
    }

    public void Complete(DateTimeOffset? atUtc = null, bool partial = false)
    {
        EnsureTransitionAllowed(partial ? IngestionJobStatus.PartiallyCompleted : IngestionJobStatus.Completed);
        var completedAt = atUtc ?? DateTimeOffset.UtcNow;
        _status = partial ? IngestionJobStatus.PartiallyCompleted : IngestionJobStatus.Completed;
        _completedAtUtc = completedAt;
        _failureReason = null;
    }

    public void Fail(string reason, DateTimeOffset? atUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureTransitionAllowed(IngestionJobStatus.Failed);
        _status = IngestionJobStatus.Failed;
        _completedAtUtc = atUtc ?? DateTimeOffset.UtcNow;
        _failureReason = reason.Length <= 2000 ? reason : reason[..2000];
    }

    public void Cancel(string reason = "Cancelled by user.", DateTimeOffset? atUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureTransitionAllowed(IngestionJobStatus.Cancelled);
        var completedAt = atUtc ?? DateTimeOffset.UtcNow;
        _status = IngestionJobStatus.Cancelled;
        _completedAtUtc = completedAt;
        _failureReason = reason;
        _currentStage = "cancelled";
        _stageSetAtUtc = completedAt;
    }

    public void MarkDeleting()
    {
        if (_status is not (IngestionJobStatus.Queued or IngestionJobStatus.AwaitingMetadata
            or IngestionJobStatus.Completed or IngestionJobStatus.Failed
            or IngestionJobStatus.Cancelled or IngestionJobStatus.PartiallyCompleted))
        {
            throw new InvalidOperationException($"A job in {_status} state cannot be marked for deletion.");
        }

        _status = IngestionJobStatus.Deleting;
    }

    public void QueueForRetry()
    {
        if (_status is not (IngestionJobStatus.Failed or IngestionJobStatus.Cancelled or IngestionJobStatus.AwaitingMetadata))
        {
            throw new InvalidOperationException($"Only failed, cancelled, or metadata-paused jobs can be retried; current status is {_status}.");
        }

        _status = IngestionJobStatus.Queued;
        _startedAtUtc = null;
        _completedAtUtc = null;
        _failureReason = null;
        _currentStage = null;
        _stageSetAtUtc = null;
        ExpectedChunkCount = null;
        IndexedChunkCount = null;
        _metadataJson = null;
        ErrorMessage = null;
        ErrorsJson = null;
    }

    public void RecordFailure(string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        ErrorsJson = detail.Trim();
        ErrorMessage = detail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? detail.Trim();
        _failureReason = detail.Length <= 2000 ? detail : detail[..2000];
    }

    public void RollbackDeletion(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (_status != IngestionJobStatus.Deleting)
        {
            throw new InvalidOperationException("Only a deleting job can be rolled back.");
        }

        _status = IngestionJobStatus.Failed;
        _failureReason = reason.Length <= 2000 ? reason : reason[..2000];
    }

    private void EnsureTransitionAllowed(IngestionJobStatus target)
    {
        if (IsTerminal(_status) && target != IngestionJobStatus.Deleting)
        {
            throw new InvalidOperationException($"A job in {_status} state cannot transition to {target}.");
        }
    }

    private static bool IsTerminal(IngestionJobStatus status) => status is IngestionJobStatus.Completed
        or IngestionJobStatus.Failed or IngestionJobStatus.Cancelled
        or IngestionJobStatus.PartiallyCompleted or IngestionJobStatus.Deleting;
}
