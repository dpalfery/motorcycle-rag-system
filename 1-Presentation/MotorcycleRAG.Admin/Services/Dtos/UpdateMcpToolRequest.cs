namespace MotorcycleRAG.Admin.Services.Dtos;

/// <summary>
/// Request model for updating MCP tool configuration
/// </summary>
public class UpdateMcpToolRequest
{
    public bool? IsEnabled { get; set; }
    public string? ChangeReason { get; set; }
}
