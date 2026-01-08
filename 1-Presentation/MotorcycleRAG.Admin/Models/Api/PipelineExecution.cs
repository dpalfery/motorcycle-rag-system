using System;
using System.Collections.ObjectModel;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Pipeline execution information
/// </summary>
public class PipelineExecution
{
    internal string ExecutionId { get; set; } = string.Empty;
    internal string PipelineType { get; set; } = string.Empty;
    internal PipelineStatus Status { get; set; }
    internal DateTime StartTime { get; set; }
    internal DateTime? EndTime { get; set; }
    internal string CreatedBy { get; set; } = string.Empty;
    internal Collection<string> Errors { get; } = new();
    internal Collection<string> Warnings { get; } = new();
}
