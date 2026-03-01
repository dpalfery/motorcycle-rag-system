namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>Coverage statistics for a manual PDF ingestion job (FR-010a).</summary>
public sealed record IngestionCoverageMetrics
{
    public double ViewablePagesPercent { get; init; }
    public double SearchableTextPagesPercent { get; init; }
    public int MissingPagesCount { get; init; }
}
