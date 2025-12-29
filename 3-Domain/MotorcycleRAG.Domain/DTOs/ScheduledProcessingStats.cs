using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Scheduled processing statistics
/// </summary>
public class ScheduledProcessingStats
{
    public DateTime? LastExecutionTime { get; set; }

    public DateTime? NextExecutionTime { get; set; }

    public PipelineStatus LastExecutionStatus { get; set; } = PipelineStatus.Completed;

    public int TotalScheduledRuns { get; set; }

    public int SuccessfulRuns { get; set; }

    public int FailedRuns { get; set; }

    public TimeSpan AverageProcessingTime { get; set; }

    public int FilesProcessedInLastRun { get; set; }

    public string LastErrorMessage { get; set; } = string.Empty;

    public Dictionary<string, object> AdditionalStats { get; set; } = new();

    /// <summary>
    /// Number of scheduled runs in the last 24 hours
    /// </summary>
    public int Last24HourRuns { get; set; }

    /// <summary>
    /// Number of successful runs in the last 24 hours
    /// </summary>
    public int Last24HourSuccesses { get; set; }

    /// <summary>
    /// Number of failed runs in the last 24 hours
    /// </summary>
    public int Last24HourFailures { get; set; }

    /// <summary>
    /// Number of cancelled runs in the last 24 hours
    /// </summary>
    public int Last24HourCancellations { get; set; }

    /// <summary>
    /// Total documents processed in the last 24 hours
    /// </summary>
    public int DocumentsProcessedLast24Hours { get; set; }

    /// <summary>
    /// Total documents indexed in the last 24 hours
    /// </summary>
    public int DocumentsIndexedLast24Hours { get; set; }
}
