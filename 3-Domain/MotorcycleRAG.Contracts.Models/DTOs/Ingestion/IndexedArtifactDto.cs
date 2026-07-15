namespace MotorcycleRAG.Contracts.Models.DTOs.Ingestion;

using MotorcycleRAG.Domain.Enums;

/// <summary>
/// Shared data-transfer shape for an indexed ingestion artifact.
/// </summary>
public class IndexedArtifactDto
{
    public Guid IndexedArtifactId { get; set; }
    public Guid IngestionJobId { get; set; }
    public string UploadId { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = string.Empty;
    public string BlobContainer { get; set; } = string.Empty;
    public string BlobPath { get; set; } = string.Empty;
    public string? SourceFileName { get; set; }
    public IndexedArtifactState State { get; set; }
    public int? ExpectedChunkCount { get; set; }
    public int? IndexedChunkCount { get; set; }
    public int? FailedChunkCount { get; set; }
    public DateTimeOffset? LastProcessedAtUtc { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
