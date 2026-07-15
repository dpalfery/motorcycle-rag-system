namespace MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

/// <summary>Data returned for a rendered manual page and its extracted text metadata.</summary>
public sealed class ManualPageDto
{
    public Guid ManualPageId { get; init; } = Guid.NewGuid();
    public Guid ManualDocumentId { get; init; }
    public int PageNumber { get; init; }
    public string BlobKey { get; init; } = string.Empty;
    public string? ExtractedText { get; init; }
    public bool IsOcrExtracted { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
