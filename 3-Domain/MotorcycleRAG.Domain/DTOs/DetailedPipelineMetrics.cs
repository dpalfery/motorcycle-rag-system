using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Detailed pipeline metrics
/// </summary>
public class DetailedPipelineMetrics {
    public TimeSpan TimeWindow { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public ExecutionMetrics Executions { get; set; } = new();

    public ProcessingMetrics Processing { get; set; } = new();

    public ErrorMetrics Errors { get; set; } = new();

    public ExecutionPerformanceMetrics Performance { get; set; } = new();

    public Collection<TrendDataPoint> TrendData { get; } = new();
}

/// <summary>
/// Execution metrics
/// </summary>
public class ExecutionMetrics {
    public int TotalExecutions { get; set; }

    public int SuccessfulExecutions { get; set; }

    public int FailedExecutions { get; set; }

    public int CancelledExecutions { get; set; }

    public double SuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions * 100 : 0;

    public Dictionary<string, int> ExecutionsByType { get; } = new();
}

/// <summary>
/// Processing metrics
/// </summary>
public class ProcessingMetrics {
    public long TotalDocumentsProcessed { get; set; }

    public long TotalDocumentsIndexed { get; set; }

    public long TotalBytesProcessed { get; set; }

    public Dictionary<FileType, long> DocumentsByType { get; } = new();

    public Dictionary<FileType, long> BytesByType { get; } = new();
}

/// <summary>
/// Error metrics
/// </summary>
public class ErrorMetrics {
    public int TotalErrors { get; set; }

    public int TotalWarnings { get; set; }

    public Dictionary<string, int> ErrorsByType { get; } = new();

    public Dictionary<string, int> ErrorsByPipeline { get; } = new();

    public Collection<ErrorSummary> TopErrors { get; } = new();
}

/// <summary>
/// Execution performance metrics
/// </summary>
public class ExecutionPerformanceMetrics {
    public TimeSpan AverageExecutionTime { get; set; }

    public TimeSpan MedianExecutionTime { get; set; }

    public TimeSpan P95ExecutionTime { get; set; }

    public double AverageDocumentsPerSecond { get; set; }

    public double AverageBytesPerSecond { get; set; }

    public Dictionary<string, TimeSpan> ExecutionTimeByType { get; } = new();
}

/// <summary>
/// Trend data point for metrics over time
/// </summary>
public class TrendDataPoint {
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
public class ErrorSummary {
    public string ErrorType { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public int Count { get; set; }

    public DateTime FirstOccurrence { get; set; }

    public DateTime LastOccurrence { get; set; }

    public Collection<string> AffectedExecutions { get; } = new();
}
