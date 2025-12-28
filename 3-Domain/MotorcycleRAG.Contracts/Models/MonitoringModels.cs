using System.Text.Json.Serialization;

namespace MotorcycleRAG.Contracts.Models;

/// <summary>
/// Pipeline notification model
/// </summary>
public class PipelineNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    public PipelineNotificationType Type { get; set; }
    
    public string Title { get; set; } = string.Empty;
    
    public string Message { get; set; } = string.Empty;
    
    public string ExecutionId { get; set; } = string.Empty;
    
    public PipelineType PipelineType { get; set; }
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    public NotificationSeverity Severity { get; set; }
    
    public Dictionary<string, object> Properties { get; set; } = new();
    
    public List<string> Recipients { get; set; } = new();
}

/// <summary>
/// Pipeline notification types
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineNotificationType
{
    ExecutionStarted,
    ExecutionCompleted,
    ExecutionFailed,
    ExecutionCancelled,
    HighFailureRate,
    LongRunningExecution,
    SystemAlert
}

/// <summary>
/// Notification severity levels
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Pipeline health status
/// </summary>
public class PipelineHealthStatus
{
    public OverallHealthStatus Status { get; set; }
    
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    public List<HealthCheck> HealthChecks { get; set; } = new();
    
    public Dictionary<string, object> Metrics { get; set; } = new();
    
    public List<string> Alerts { get; set; } = new();
}

/// <summary>
/// Individual health check result
/// </summary>
public class HealthCheck
{
    public string Name { get; set; } = string.Empty;
    
    public HealthCheckStatus Status { get; set; }
    
    public string Description { get; set; } = string.Empty;
    
    public TimeSpan Duration { get; set; }
    
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    
    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>
/// Health check status enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HealthCheckStatus
{
    Healthy,
    Degraded,
    Unhealthy
}

/// <summary>
/// Overall health status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OverallHealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}

/// <summary>
/// Detailed pipeline metrics
/// </summary>
public class DetailedPipelineMetrics
{
    public TimeSpan TimeWindow { get; set; }
    
    public DateTime StartTime { get; set; }
    
    public DateTime EndTime { get; set; }
    
    public ExecutionMetrics Executions { get; set; } = new();
    
    public ProcessingMetrics Processing { get; set; } = new();
    
    public ErrorMetrics Errors { get; set; } = new();
    
    public ExecutionPerformanceMetrics Performance { get; set; } = new();
    
    public List<TrendDataPoint> TrendData { get; set; } = new();
}

/// <summary>
/// Execution metrics
/// </summary>
public class ExecutionMetrics
{
    public int TotalExecutions { get; set; }
    
    public int SuccessfulExecutions { get; set; }
    
    public int FailedExecutions { get; set; }
    
    public int CancelledExecutions { get; set; }
    
    public double SuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions * 100 : 0;
    
    public Dictionary<PipelineType, int> ExecutionsByType { get; set; } = new();
}

/// <summary>
/// Processing metrics
/// </summary>
public class ProcessingMetrics
{
    public long TotalDocumentsProcessed { get; set; }
    
    public long TotalDocumentsIndexed { get; set; }
    
    public long TotalBytesProcessed { get; set; }
    
    public Dictionary<FileType, long> DocumentsByType { get; set; } = new();
    
    public Dictionary<FileType, long> BytesByType { get; set; } = new();
}

/// <summary>
/// Error metrics
/// </summary>
public class ErrorMetrics
{
    public int TotalErrors { get; set; }
    
    public int TotalWarnings { get; set; }
    
    public Dictionary<string, int> ErrorsByType { get; set; } = new();
    
    public Dictionary<string, int> ErrorsByPipeline { get; set; } = new();
    
    public List<ErrorSummary> TopErrors { get; set; } = new();
}

/// <summary>
/// Execution performance metrics
/// </summary>
public class ExecutionPerformanceMetrics
{
    public TimeSpan AverageExecutionTime { get; set; }
    
    public TimeSpan MedianExecutionTime { get; set; }
    
    public TimeSpan P95ExecutionTime { get; set; }
    
    public double AverageDocumentsPerSecond { get; set; }
    
    public double AverageBytesPerSecond { get; set; }
    
    public Dictionary<PipelineType, TimeSpan> ExecutionTimeByType { get; set; } = new();
}

/// <summary>
/// Trend data point for metrics over time
/// </summary>
public class TrendDataPoint
{
    public DateTime Timestamp { get; set; }
    
    public int Executions { get; set; }
    
    public int SuccessfulExecutions { get; set; }
    
    public int FailedExecutions { get; set; }
    
    public TimeSpan AverageExecutionTime { get; set; }
    
    public long DocumentsProcessed { get; set; }
}

/// <summary>
/// Error summary information
/// </summary>
public class ErrorSummary
{
    public string ErrorType { get; set; } = string.Empty;
    
    public string ErrorMessage { get; set; } = string.Empty;
    
    public int Count { get; set; }
    
    public DateTime FirstOccurrence { get; set; }
    
    public DateTime LastOccurrence { get; set; }
    
    public List<string> AffectedExecutions { get; set; } = new();
}

/// <summary>
/// Pipeline alert configuration
/// </summary>
public class PipelineAlertConfig
{
    public bool IsEnabled { get; set; } = true;
    
    public AlertThresholds Thresholds { get; set; } = new();
    
    public List<string> EmailRecipients { get; set; } = new();
    
    public List<string> SlackChannels { get; set; } = new();
    
    public Dictionary<NotificationSeverity, bool> EnabledSeverities { get; set; } = new()
    {
        [NotificationSeverity.Info] = false,
        [NotificationSeverity.Warning] = true,
        [NotificationSeverity.Error] = true,
        [NotificationSeverity.Critical] = true
    };
    
    public TimeSpan AlertCooldown { get; set; } = TimeSpan.FromMinutes(15);
    
    public Dictionary<string, object> CustomSettings { get; set; } = new();
}

/// <summary>
/// Alert threshold configuration
/// </summary>
public class AlertThresholds
{
    public double FailureRateThreshold { get; set; } = 0.10; // 10% failure rate
    
    public TimeSpan LongRunningExecutionThreshold { get; set; } = TimeSpan.FromMinutes(30);
    
    public int ConsecutiveFailuresThreshold { get; set; } = 3;
    
    public TimeSpan HealthCheckFailureThreshold { get; set; } = TimeSpan.FromMinutes(5);
    
    public long MaxQueueSizeThreshold { get; set; } = 1000;
    
    public Dictionary<string, double> CustomThresholds { get; set; } = new();
}
