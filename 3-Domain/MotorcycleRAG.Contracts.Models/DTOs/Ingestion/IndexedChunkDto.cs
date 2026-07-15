namespace MotorcycleRAG.Contracts.Models.DTOs.Ingestion;

using MotorcycleRAG.Domain.Enums;

/// <summary>
/// Shared data-transfer shape for an indexed ingestion chunk.
/// </summary>
public class IndexedChunkDto
{
    public string ChunkId { get; set; } = string.Empty;
    public Guid IndexedArtifactId { get; set; }
    public Guid IngestionJobId { get; set; }
    public string UploadId { get; set; } = string.Empty;
    public string? SourceFileName { get; set; }
    public int? PageNumber { get; set; }
    public int? ChunkIndex { get; set; }
    public string? Stage { get; set; }
    public ChunkIndexStatus Status { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public string? FailureReason { get; set; }
}
