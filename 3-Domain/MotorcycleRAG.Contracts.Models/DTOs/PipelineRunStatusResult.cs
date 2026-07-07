namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Status payload returned when polling a local or Fabric pipeline run.
/// </summary>
public sealed record PipelineRunStatusResult
{
    public string Status { get; init; } = string.Empty;
    public string? Message { get; init; }
    public string? Error { get; init; }
    public string? RawJson { get; init; }

    public static PipelineRunStatusResult FromStatus(string status) =>
        new() { Status = status };
}
