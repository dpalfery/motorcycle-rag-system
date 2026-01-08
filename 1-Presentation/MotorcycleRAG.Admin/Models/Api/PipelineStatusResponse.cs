using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Response for get pipeline status operation
/// </summary>
public class PipelineStatusResponse
{
    internal string ExecutionId { get; set; } = string.Empty;
    internal PipelineStatus Status { get; set; }
}
