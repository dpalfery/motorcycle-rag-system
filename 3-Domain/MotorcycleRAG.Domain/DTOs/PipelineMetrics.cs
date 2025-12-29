using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

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
