namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Describes a blob object returned from storage listing operations.
/// </summary>
public sealed record BlobObjectDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string? ContentType { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset? LastModifiedUtc { get; init; }
}
