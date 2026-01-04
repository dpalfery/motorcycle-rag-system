using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Result of pipeline execution
/// </summary>
public class PipelineExecutionResult {
    public string ExecutionId { get; set; } = string.Empty;

    public PipelineStatus Status { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public TimeSpan Duration => EndTime?.Subtract(StartTime) ?? TimeSpan.Zero;

    public ProcessedData? ProcessedData { get; set; }

    public IndexingResult? IndexingResult { get; set; }

    public Collection<string> Errors { get; } = new();

    public Collection<string> Warnings { get; } = new();

    public Dictionary<string, object> Metrics { get; } = new();

    public string Message { get; set; } = string.Empty;
}

