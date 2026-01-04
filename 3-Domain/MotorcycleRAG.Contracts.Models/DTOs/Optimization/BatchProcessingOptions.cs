namespace MotorcycleRAG.Domain.DTOs.Optimization;

/// <summary>
/// Options for batch processing configuration.
/// </summary>
public class BatchProcessingOptions {
    public int BatchSize { get; set; } = 100;
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public bool EnableRetry { get; set; } = true;
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public bool EnableProgressReporting { get; set; } = true;
    public long MaxMemoryUsage { get; set; } = 1024 * 1024 * 1024; // 1GB default
}
