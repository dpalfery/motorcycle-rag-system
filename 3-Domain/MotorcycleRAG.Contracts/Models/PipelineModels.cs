using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MotorcycleRAG.Contracts.Models;

/// <summary>
/// Request model for data pipeline processing
/// </summary>
public class DataPipelineRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Required]
    public string FileName { get; set; } = string.Empty;
    
    [Required]
    public string FilePath { get; set; } = string.Empty;
    
    [Required]
    public FileType FileType { get; set; }
    
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    public PipelineOptions Options { get; set; } = new();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Result of pipeline execution
/// </summary>
public class PipelineExecutionResult
{
    public string ExecutionId { get; set; } = string.Empty;
    
    public PipelineStatus Status { get; set; }
    
    public DateTime StartTime { get; set; }
    
    public DateTime? EndTime { get; set; }
    
    public TimeSpan Duration => EndTime?.Subtract(StartTime) ?? TimeSpan.Zero;
    
    public ProcessedData? ProcessedData { get; set; }
    
    public IndexingResult? IndexingResult { get; set; }
    
    public List<string> Errors { get; set; } = new();
    
    public List<string> Warnings { get; set; } = new();
    
    public Dictionary<string, object> Metrics { get; set; } = new();
    
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Batch pipeline processing result
/// </summary>
public class BatchPipelineResult
{
    public string BatchId { get; set; } = Guid.NewGuid().ToString();
    
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    
    public DateTime? EndTime { get; set; }
    
    public int TotalFiles { get; set; }
    
    public int ProcessedSuccessfully { get; set; }
    
    public int ProcessedWithErrors { get; set; }
    
    public int Failed { get; set; }
    
    public List<PipelineExecutionResult> Results { get; set; } = new();
    
    public Dictionary<string, object> BatchMetrics { get; set; } = new();
    
    public bool IsCompleted => EndTime.HasValue;
    
    public bool HasErrors => Failed > 0 || ProcessedWithErrors > 0;
}

/// <summary>
/// Pipeline execution status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineStatus
{
    Queued,
    Processing,
    Indexing,
    Completed,
    Failed,
    Cancelled,
    PartiallyCompleted
}

/// <summary>
/// Pipeline execution metrics
/// </summary>
public class PipelineMetrics
{
    public int TotalExecutions { get; set; }
    
    public int SuccessfulExecutions { get; set; }
    
    public int FailedExecutions { get; set; }
    
    public TimeSpan AverageExecutionTime { get; set; }
    
    public long TotalDocumentsProcessed { get; set; }
    
    public long TotalDocumentsIndexed { get; set; }
    
    public DateTime MetricsStartTime { get; set; }
    
    public DateTime MetricsEndTime { get; set; }
    
    public Dictionary<FileType, int> ProcessingByFileType { get; set; } = new();
    
    public Dictionary<PipelineStatus, int> ExecutionsByStatus { get; set; } = new();
    
    public List<PipelineExecutionSummary> RecentExecutions { get; set; } = new();
}

/// <summary>
/// Pipeline execution summary
/// </summary>
public class PipelineExecutionSummary
{
    public string ExecutionId { get; set; } = string.Empty;
    
    public string FileName { get; set; } = string.Empty;
    
    public FileType FileType { get; set; }
    
    public PipelineStatus Status { get; set; }
    
    public DateTime StartTime { get; set; }
    
    public TimeSpan Duration { get; set; }
    
    public int DocumentsProcessed { get; set; }
    
    public int DocumentsIndexed { get; set; }
    
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Pipeline processing options
/// </summary>
public class PipelineOptions
{
    public bool ProcessImages { get; set; } = true;
    
    public bool GenerateEmbeddings { get; set; } = true;
    
    public bool IndexImmediately { get; set; } = true;
    
    public int BatchSize { get; set; } = 100;
    
    public int MaxRetries { get; set; } = 3;
    
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);
    
    public Dictionary<string, object> CustomOptions { get; set; } = new();
}

/// <summary>
/// File types supported by the pipeline
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FileType
{
    CSV,
    PDF,
    Unknown
}

/// <summary>
/// Pipeline type enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineType
{
    CSV,
    PDF,
    Batch,
    Scheduled
}

/// <summary>
/// Pipeline execution context
/// </summary>
public class PipelineExecutionContext
{
    public string UserId { get; set; } = string.Empty;
    
    public string SessionId { get; set; } = string.Empty;
    
    public string CorrelationId { get; set; } = string.Empty;
    
    public Dictionary<string, string> Properties { get; set; } = new();
    
    public DateTime RequestTime { get; set; } = DateTime.UtcNow;
    
    public string Source { get; set; } = "API";
}
