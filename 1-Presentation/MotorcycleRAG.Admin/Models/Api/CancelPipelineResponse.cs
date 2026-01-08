namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Response for cancel pipeline operation
/// </summary>
public class CancelPipelineResponse
{
    internal string ExecutionId { get; set; } = string.Empty;
    internal bool Cancelled { get; set; }
}
