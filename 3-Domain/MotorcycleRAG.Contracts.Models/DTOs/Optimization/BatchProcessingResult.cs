namespace MotorcycleRAG.Domain.DTOs.Optimization;

/// <summary>
/// Result of batch processing operation.
/// </summary>
public class BatchProcessingResult<T>
{
    public IReadOnlyList<T> Results { get; set; } = Array.Empty<T>();
    public IReadOnlyList<BatchProcessingError> Errors { get; set; } = Array.Empty<BatchProcessingError>();
    public int TotalProcessed { get; set; }
    public int SuccessfullyProcessed { get; set; }
    public int Failed { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public double ThroughputPerSecond => TotalDuration.TotalSeconds > 0 ? TotalProcessed / TotalDuration.TotalSeconds : 0;
    public bool IsSuccess => Failed == 0;
}
