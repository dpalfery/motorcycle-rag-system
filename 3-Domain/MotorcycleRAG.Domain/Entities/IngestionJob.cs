using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Represents a Microsoft Fabric pipeline ingestion job.
/// Tracks the full lifecycle of a single ingestion operation from upload to completion.
/// Supports FR-009 (trigger ingestion), FR-010 (status), FR-010a (coverage reporting).
/// </summary>
public class IngestionJob
{
    /// <summary>Primary key — GUID assigned at creation time.</summary>
    public Guid IngestionJobId { get; set; } = Guid.NewGuid();

    /// <summary>UTC timestamp when the job record was created (upload accepted).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp when the Fabric pipeline run actually started. Null until pipeline starts.</summary>
    public DateTimeOffset? StartedAtUtc { get; set; }

    /// <summary>UTC timestamp when the job reached a terminal state (Completed, Failed, Cancelled).</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Entra ID subject (oid) of the user who initiated the job. Never logged raw.</summary>
    public string? CreatedBySubject { get; set; }

    /// <summary>Current job lifecycle status.</summary>
    public IngestionJobStatus Status { get; set; } = IngestionJobStatus.Queued;

    /// <summary>Human-readable failure description. Populated only on terminal failure.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Type of ingestion input (PDFManual, StructuredSpecification, etc.).</summary>
    public IngestionJobType InputType { get; set; }

    /// <summary>
    /// Opaque reference to the uploaded blob (blob key or upload GUID).
    /// Never a filesystem path. Format: manuals/{guid}/upload/{filename} or uploads/{guid}.
    /// </summary>
    public string InputRef { get; set; } = string.Empty;

    /// <summary>Compute provider used. Defaults to "MicrosoftFabric".</summary>
    public string ComputeProvider { get; set; } = "MicrosoftFabric";

    /// <summary>Microsoft Fabric pipeline run ID for status polling and audit. Null until pipeline is triggered.</summary>
    public string? FabricRunId { get; set; }

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
    public string? MetadataJson { get; set; }
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
}
