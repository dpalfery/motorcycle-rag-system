using System;
using System.Collections.ObjectModel;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Pipeline execution information
/// </summary>
#pragma warning disable CA1812 // Instantiated via deserialization
#pragma warning disable S3059 // Public properties required for serialization
internal class PipelineExecution
{
    public string ExecutionId { get; set; } = string.Empty;
#pragma warning restore S3059
    public string PipelineType { get; set; } = string.Empty;
    public PipelineStatus Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public Collection<string> Errors { get; } = new();
    public Collection<string> Warnings { get; } = new();
}
