namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Response returned by bulk ingestion cleanup actions.
/// </summary>
public sealed record IngestionCleanupResponse
{
    public string Scope { get; init; } = string.Empty;
    public int DeletedCount { get; init; }
}
