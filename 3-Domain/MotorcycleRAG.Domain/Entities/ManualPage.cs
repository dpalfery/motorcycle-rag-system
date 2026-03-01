namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Represents a single rendered page from a motorcycle manual.
/// Stores the blob reference for the PNG asset and extracted text for search.
/// Supports FR-011 (manual page retrieval) and FR-007 (vector search citations).
/// </summary>
public class ManualPage
{
    /// <summary>Primary key — GUID assigned at creation.</summary>
    public Guid ManualPageId { get; set; } = Guid.NewGuid();

    /// <summary>GUID of the parent MotorcycleManual.</summary>
    public Guid ManualDocumentId { get; set; }

    /// <summary>1-based page number within the manual.</summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Blob storage key for the rendered PNG image of this page.
    /// Pattern: manuals/{manualDocumentId}/pages/{pageNumber}.png
    /// </summary>
    public string BlobKey { get; set; } = string.Empty;

    /// <summary>Text content extracted from this page (OCR or native). Used for search indexing.</summary>
    public string? ExtractedText { get; set; }

    /// <summary>Whether this page's text was extracted via OCR (true) or native PDF text (false).</summary>
    public bool IsOcrExtracted { get; set; }

    /// <summary>UTC timestamp when this page record was created by the Fabric pipeline.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
