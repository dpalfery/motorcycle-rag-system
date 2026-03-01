namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>Workload limit constants returned with each job status response.</summary>
public sealed record IngestionWorkloadLimits
{
    public int MaxPages { get; init; }
    public long MaxInputBytes { get; init; }
    public int MaxRuntimeMinutes { get; init; }
}
