namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Response shape for blob-backed source files that have not completed ingestion yet.
/// </summary>
public sealed record PendingStorageFileDto
{
    public string UploadId { get; init; } = string.Empty;
    public string BlobName { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset LastModifiedUtc { get; init; }
    public string? LastKnownJobStatus { get; init; }
    public string? FailureReason { get; init; }
    public string? GraphImportStatus { get; init; }
    public string? GraphImportFailureReason { get; init; }
}
