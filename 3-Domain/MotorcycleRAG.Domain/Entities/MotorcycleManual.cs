namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Represents a motorcycle service manual document ingested via Fabric pipeline.
/// Acts as the aggregate root for all pages and graph relationships extracted from a single PDF.
/// Supports FR-008 (manual ingestion) and FR-011 (manual page retrieval).
/// </summary>
public class MotorcycleManual
{
    /// <summary>Primary key — GUID assigned at ingestion time.</summary>
    public Guid ManualDocumentId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Motorcycle make (manufacturer), e.g. "Honda", "Yamaha".
    /// Derived from PDF metadata or admin-provided during upload.
    /// </summary>
    public string Make { get; set; } = string.Empty;

    /// <summary>Motorcycle model designation, e.g. "CBR1000RR", "R1".</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Model year, e.g. 2024.</summary>
    public int? Year { get; set; }

    /// <summary>Original filename as uploaded (display only — not a filesystem path).</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Total page count as reported by Document Intelligence / Fabric pipeline.</summary>
    public int TotalPages { get; set; }

    /// <summary>UTC timestamp when this manual record was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp of last metadata update.</summary>
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    /// <summary>
    /// GUID of the ingestion job that produced this manual record.
    /// Allows tracing back to the source pipeline run.
    /// </summary>
    public Guid SourceIngestionJobId { get; set; }
}
