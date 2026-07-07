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
    public string? StatusUrl { get; init; }
}
