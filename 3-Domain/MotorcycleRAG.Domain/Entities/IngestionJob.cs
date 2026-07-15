using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Represents a Microsoft Fabric pipeline ingestion job.
/// Tracks the full lifecycle of a single ingestion operation from upload to completion.
/// Supports FR-009 (trigger ingestion), FR-010 (status), FR-010a (coverage reporting).
/// </summary>
public class IngestionJob
{
    /// <summary>SQL identity key for display and sorting.</summary>
    public long Id { get; set; }

    /// <summary>Primary key — GUID assigned at creation time.</summary>
    public Guid IngestionJobId { get; set; } = Guid.NewGuid();

    /// <summary>UTC timestamp when the job record was created (upload accepted).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp when the Fabric pipeline run actually started. Null until pipeline starts.</summary>
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _completedAtUtc;
    private IngestionJobStatus _status = IngestionJobStatus.Queued;
    private string? _failureReason;
    private string? _metadataJson;
    private string? _currentStage;
    private DateTimeOffset? _stageSetAtUtc;

    public DateTimeOffset? StartedAtUtc { get => _startedAtUtc; init => _startedAtUtc = value; }

    /// <summary>UTC timestamp when the job reached a terminal state (Completed, Failed, Cancelled).</summary>
    public DateTimeOffset? CompletedAtUtc { get => _completedAtUtc; init => _completedAtUtc = value; }

    /// <summary>Entra ID subject (oid) of the user who initiated the job. Never logged raw.</summary>
    public string? CreatedBySubject { get; set; }

    /// <summary>Current job lifecycle status.</summary>
    public IngestionJobStatus Status { get => _status; init => _status = value; }

    /// <summary>Human-readable failure description. Populated only on terminal failure.</summary>
    public string? FailureReason { get => _failureReason; init => _failureReason = value; }

    /// <summary>Type of ingestion input (PDFManual, StructuredSpecification, etc.).</summary>
    public IngestionJobType InputType { get; set; }

    /// <summary>
    /// Opaque reference to the uploaded blob (blob key or upload GUID).
    /// Never a filesystem path. Format: manuals/{guid}/upload/{filename} or uploads/{guid}.
    /// </summary>
    public string InputRef { get; set; } = string.Empty;

    /// <summary>Compute provider used. Defaults to "MicrosoftFabric".</summary>
    // vestigial: Microsoft Fabric compute provider kept for backward compatibility; LocalProcessingService is preferred
    public string ComputeProvider { get; set; } = "MicrosoftFabric";

    /// <summary>Document ingestion run ID for status polling and audit. Null until pipeline is triggered. Vestigial reference to Fabric.</summary>
    public string? DocIngestionRunId { get; set; }

    /// <summary>
    /// GUID of the MotorcycleManual document produced by this job.
    /// Populated only when InputType is PDFManual and job reaches Completed/PartiallyCompleted.
    /// </summary>
    public Guid? ManualDocumentId { get; set; }

    // --- Legacy and additional fields ---

    public string? JobId { get; set; }
    public string? JobType { get; set; }
    public string? SourceFilePath { get; set; }
    public string? SourceFileName { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public string? UserId { get; set; }
    public string? UserEmail { get; set; }
    public int TotalRecordsProcessed { get; set; }
    public int RecordsIndexed { get; set; }
    public int RecordsFailed { get; set; }
    public int RecordsWithWarnings { get; set; }
    public string? ErrorsJson { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>
    /// Motorcycle metadata extracted by the PDF pipeline's LLM or submitted manually by an
    /// admin. Stored as a JSON blob with the shape:
    /// <c>{ "make": "...", "model": "...", "year": 2023, "category": "...", "tags": [...] }</c>.
    /// Populated during the <c>extracting-metadata</c> pipeline stage. When automated extraction
    /// is incomplete the job pauses in <see cref="IngestionJobStatus.AwaitingMetadata"/>
    /// and this field holds the partial result; once an admin submits manual metadata it is
    /// overwritten with the complete blob. Column is <c>nvarchar(max)</c>.
    /// </summary>
    public string? MetadataJson { get => _metadataJson; init => _metadataJson = value; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // --- Coverage fields (populated by Fabric pipeline upon completion) ---

    /// <summary>Total pages in the ingested PDF. Null for non-PDF jobs or until pipeline reports.</summary>
    public int? TotalPages { get; set; }

    /// <summary>Pages that were captured as viewable (rendered PNG). Null until pipeline reports.</summary>
    public int? PagesCapturedViewableCount { get; set; }

    /// <summary>Pages with searchable text (OCR or native). Null until pipeline reports.</summary>
    public int? PagesWithSearchableTextCount { get; set; }

    /// <summary>Pages processed via OCR. Null until pipeline reports.</summary>
    public int? PagesWithOcrTextCount { get; set; }

    /// <summary>Pages with native embedded text (no OCR needed). Null until pipeline reports.</summary>
    public int? PagesWithNativeTextCount { get; set; }

    /// <summary>
    /// JSON array of missing page numbers (pages that could not be rendered or extracted).
    /// Stored as JSON for SQL persistence; deserialize before use.
    /// </summary>
    public string? MissingPagesJson { get; set; }

    /// <summary>
    /// JSON object with extended pipeline metrics (timing, token counts, etc.).
    /// Stored as JSON for SQL persistence; deserialize before use.
    /// </summary>
    public string? MetricsJson { get; set; }

    // --- Chunk indexing counts (populated when search-chunks artifacts are indexed) ---

    /// <summary>Expected number of chunks parsed from the JSONL artifact. Null until indexing is attempted.</summary>
    public int? ExpectedChunkCount { get; set; }

    /// <summary>Number of chunks successfully indexed into Azure Search. Null until indexing is attempted.</summary>
    public int? IndexedChunkCount { get; set; }

    // --- Stage tracking (reported by local processor during processing) ---

    /// <summary>
    /// Current pipeline stage reported by the local processor.
    /// Values: copying, parsing, chunking, embedding, uploading-chunks, extracting-graph, uploading-graph, completed.
    /// </summary>
    public string? CurrentStage { get => _currentStage; init => _currentStage = value; }

    /// <summary>UTC timestamp when the current stage was last updated.</summary>
    public DateTimeOffset? StageSetAtUtc { get => _stageSetAtUtc; init => _stageSetAtUtc = value; }

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
