using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Response for get pipeline status operation
/// </summary>
public class PipelineStatusResponse
{
    public string ExecutionId { get; set; } = string.Empty;
    public PipelineStatus Status { get; set; }
}
