namespace MotorcycleRAG.Contracts.Optimization;

/// <summary>
/// Interface for optimized batch processing of data ingestion operations.
/// </summary>
public interface IBatchProcessingService
{
    /// <summary>
    /// Processes documents in optimized batches.
    /// </summary>
    /// <typeparam name="T">Type of document to process</typeparam>
    /// <param name="documents">Documents to process</param>
    /// <param name="processor">Processing function</param>
    /// <param name="batchSize">Optimal batch size</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processing results</returns>
    Task<BatchProcessingResult<TResult>> ProcessBatchAsync<T, TResult>(
        IEnumerable<T> documents,
        Func<IEnumerable<T>, CancellationToken, Task<IEnumerable<TResult>>> processor,
        int batchSize = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes documents with parallel execution and load balancing.
    /// </summary>
    /// <typeparam name="T">Type of document to process</typeparam>
    /// <param name="documents">Documents to process</param>
    /// <param name="processor">Processing function</param>
    /// <param name="options">Processing options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processing results</returns>
    Task<BatchProcessingResult<TResult>> ProcessParallelBatchAsync<T, TResult>(
        IEnumerable<T> documents,
        Func<T, CancellationToken, Task<TResult>> processor,
        BatchProcessingOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimizes batch size based on document characteristics and system resources.
    /// </summary>
    /// <param name="documentCount">Number of documents</param>
    /// <param name="averageDocumentSize">Average document size in bytes</param>
    /// <param name="availableMemory">Available memory in bytes</param>
    /// <returns>Optimal batch size</returns>
    int OptimizeBatchSize(int documentCount, long averageDocumentSize, long availableMemory);

    /// <summary>
    /// Gets batch processing statistics for monitoring and optimization.
    /// </summary>
    /// <returns>Processing statistics</returns>
    BatchProcessingStatistics GetStatistics();

    /// <summary>
    /// Resets processing statistics.
    /// </summary>
    void ResetStatistics();
}

/// <summary>
/// Options for batch processing configuration.
/// </summary>
public class BatchProcessingOptions
{
    public int BatchSize { get; set; } = 100;
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public bool EnableRetry { get; set; } = true;
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public bool EnableProgressReporting { get; set; } = true;
    public long MaxMemoryUsage { get; set; } = 1024 * 1024 * 1024; // 1GB default
}

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

/// <summary>
/// Error information for failed batch processing items.
/// </summary>
public class BatchProcessingError
{
    public int ItemIndex { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public Exception Exception { get; set; } = null!;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

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
