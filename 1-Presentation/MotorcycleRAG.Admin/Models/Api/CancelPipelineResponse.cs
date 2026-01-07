namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Response for cancel pipeline operation
/// </summary>
public class CancelPipelineResponse
{
    public string ExecutionId { get; set; } = string.Empty;
    public bool Cancelled { get; set; }
}
