using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

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
