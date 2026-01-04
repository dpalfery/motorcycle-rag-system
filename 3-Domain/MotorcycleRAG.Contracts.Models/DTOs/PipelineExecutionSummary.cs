using System;

namespace MotorcycleRAG.Contracts.Models.DTOs;

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

