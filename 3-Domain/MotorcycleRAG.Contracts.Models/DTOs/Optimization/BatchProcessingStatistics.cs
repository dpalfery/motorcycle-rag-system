namespace MotorcycleRAG.Domain.DTOs.Optimization;

/// <summary>
/// Statistics for batch processing operations.
/// </summary>
public class BatchProcessingStatistics
{
    public long TotalBatchesProcessed { get; set; }
    public long TotalItemsProcessed { get; set; }
    public long TotalItemsFailed { get; set; }
    public TimeSpan TotalProcessingTime { get; set; }
    public double AverageThroughputPerSecond => TotalProcessingTime.TotalSeconds > 0 ? TotalItemsProcessed / TotalProcessingTime.TotalSeconds : 0;
    public double SuccessRate => TotalItemsProcessed > 0 ? (double)(TotalItemsProcessed - TotalItemsFailed) / TotalItemsProcessed : 0;
    public int OptimalBatchSize { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
