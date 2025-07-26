using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Document for batch processing
/// </summary>
public class ProcessingDocument
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    public Dictionary<string, object> Metadata { get; set; } = new();

    public string DocumentType { get; set; } = string.Empty;

    public long SizeBytes => System.Text.Encoding.UTF8.GetByteCount(Content);
}

/// <summary>
/// Result of batch processing operation
/// </summary>
public class BatchProcessingResult
{
    /// <summary>
    /// Total number of documents processed
    /// </summary>
    public int TotalDocuments { get; set; }

    /// <summary>
    /// Number of successfully processed documents
    /// </summary>
    public int SuccessfullyProcessed { get; set; }

    /// <summary>
    /// Number of failed documents
    /// </summary>
    public int Failed { get; set; }

    /// <summary>
    /// Processing errors
    /// </summary>
    public List<ProcessingError> Errors { get; set; } = new();

    /// <summary>
    /// Total processing time
    /// </summary>
    public TimeSpan ProcessingTime { get; set; }

    /// <summary>
    /// Average processing time per document
    /// </summary>
    public TimeSpan AverageTimePerDocument => TotalDocuments > 0 
        ? TimeSpan.FromMilliseconds(ProcessingTime.TotalMilliseconds / TotalDocuments) 
        : TimeSpan.Zero;

    /// <summary>
    /// Processing throughput (documents per second)
    /// </summary>
    public double ThroughputPerSecond => ProcessingTime.TotalSeconds > 0 
        ? TotalDocuments / ProcessingTime.TotalSeconds 
        : 0.0;

    /// <summary>
    /// Memory usage during processing
    /// </summary>
    public long PeakMemoryUsageBytes { get; set; }
}

/// <summary>
/// Batch processing indexing result
/// </summary>
public class BatchProcessingIndexingResult
{
    /// <summary>
    /// Total number of documents indexed
    /// </summary>
    public int TotalDocuments { get; set; }

    /// <summary>
    /// Number of successfully indexed documents
    /// </summary>
    public int SuccessfullyIndexed { get; set; }

    /// <summary>
    /// Number of failed documents
    /// </summary>
    public int Failed { get; set; }

    /// <summary>
    /// Indexing errors
    /// </summary>
    public List<ProcessingError> Errors { get; set; } = new();

    /// <summary>
    /// Total indexing time
    /// </summary>
    public TimeSpan IndexingTime { get; set; }

    /// <summary>
    /// Index statistics
    /// </summary>
    public IndexStatistics Statistics { get; set; } = new();
}

/// <summary>
/// Processing error details
/// </summary>
public class ProcessingError
{
    [Required]
    public string DocumentId { get; set; } = string.Empty;

    [Required]
    public string ErrorMessage { get; set; } = string.Empty;

    public string ErrorCode { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public Dictionary<string, object> AdditionalInfo { get; set; } = new();
}

/// <summary>
/// Index statistics
/// </summary>
public class IndexStatistics
{
    /// <summary>
    /// Total number of documents in index
    /// </summary>
    public long TotalDocuments { get; set; }

    /// <summary>
    /// Index size in bytes
    /// </summary>
    public long IndexSizeBytes { get; set; }

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Average document size in bytes
    /// </summary>
    public long AverageDocumentSize => TotalDocuments > 0 ? IndexSizeBytes / TotalDocuments : 0;
}

/// <summary>
/// Configuration for batch processing optimization
/// </summary>
public class BatchProcessingConfiguration
{
    /// <summary>
    /// Default batch size for document processing
    /// </summary>
    [Range(10, 1000)]
    public int DefaultBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum batch size allowed
    /// </summary>
    [Range(100, 5000)]
    public int MaxBatchSize { get; set; } = 1000;

    /// <summary>
    /// Maximum memory usage per batch in MB
    /// </summary>
    [Range(50, 2000)]
    public int MaxMemoryPerBatchMB { get; set; } = 500;

    /// <summary>
    /// Number of parallel processing threads
    /// </summary>
    [Range(1, 10)]
    public int ParallelProcessingThreads { get; set; } = 3;

    /// <summary>
    /// Timeout for batch processing operations
    /// </summary>
    public TimeSpan BatchProcessingTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Enable batch processing optimization
    /// </summary>
    public bool EnableOptimization { get; set; } = true;

    /// <summary>
    /// Retry configuration for failed batches
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;
}