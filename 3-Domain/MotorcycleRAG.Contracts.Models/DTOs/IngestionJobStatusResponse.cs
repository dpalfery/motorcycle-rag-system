namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Response shape for GET /api/ingestion/jobs/{jobId}.
/// Supports FR-010 (job status) and FR-010a (coverage reporting).
/// </summary>
public sealed record IngestionJobStatusResponse
{
    public long Id { get; init; }
    public Guid JobId { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public string InputType { get; init; } = string.Empty;
    public string InputRef { get; init; } = string.Empty;

    /// <summary>
    /// Original filename of the uploaded document, if provided when the job was started.
    /// </summary>
    public string? SourceFileName { get; init; }
    public string? ComputeProvider { get; init; }
    public Guid? ManualDocumentId { get; init; }
    public int? TotalPages { get; init; }
    public int? PagesCapturedViewableCount { get; init; }
    public int? PagesWithSearchableTextCount { get; init; }
    public int? PagesWithOcrTextCount { get; init; }
    public int? PagesWithNativeTextCount { get; init; }
    public IReadOnlyList<int> MissingPages { get; init; } = [];
    public IngestionCoverageMetrics? Coverage { get; init; }
    public IngestionWorkloadLimits? WorkloadLimits { get; init; }
    public string? Message { get; init; }
    public string? FailureReason { get; init; }
    /// <summary>Full failure diagnostics (stack traces, processor payload). Local admin only.</summary>
    public string? FailureDetail { get; init; }
    public string? DocIngestionRunId { get; init; }
    public int? ExpectedChunkCount { get; init; }
    public int? IndexedChunkCount { get; init; }
    public string? CurrentStage { get; init; }
    public DateTimeOffset? StageSetAtUtc { get; init; }
    /// <summary>
    /// Indicates the job is paused awaiting manually-entered metadata. True when the job
    /// status is <c>AwaitingMetadata</c> or the current stage is <c>needs-manual-metadata</c>.
    /// Replaces fragile client-side substring sniffing of <see cref="FailureReason"/>.
    /// </summary>
    public bool RequiresManualMetadata { get; init; }
    public string? StatusUrl { get; init; }

    /// <summary>
    /// Parsed motorcycle metadata — populated from <c>job.MetadataJson</c> when present.
    /// All fields are nullable because metadata may not have been extracted yet (e.g.
    /// the job is still queued or in an early pipeline stage). When <c>MetadataJson</c>
    /// is null/empty/invalid, every field below is null.
    /// </summary>
    public string? Make { get; init; }
    public string? Model { get; init; }
    public int? Year { get; init; }
    public string? Category { get; init; }

    /// <summary>Free-form tags extracted from the document. Null when no metadata is present.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Fraction (0.0–1.0) of the four required fields (make/model/year/category) populated.</summary>
    public double? FillRate { get; init; }

    /// <summary>True when all four required fields are populated; false otherwise.</summary>
    public bool? IsComplete { get; init; }
}
