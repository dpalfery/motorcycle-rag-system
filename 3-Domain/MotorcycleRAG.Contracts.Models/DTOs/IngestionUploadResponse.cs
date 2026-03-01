namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Response shape for POST /api/ingestion/jobs/upload (202 Accepted).
/// The uploadId is an opaque reference — never a filesystem path.
/// </summary>
public sealed record IngestionUploadResponse
{
    public string UploadId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public string Status { get; init; } = "pending-ingestion";
}
