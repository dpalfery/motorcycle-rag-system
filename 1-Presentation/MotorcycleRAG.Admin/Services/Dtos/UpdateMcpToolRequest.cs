namespace MotorcycleRAG.Admin.Services.Dtos;

/// <summary>
/// Request model for updating MCP tool configuration
/// </summary>
internal class UpdateMcpToolRequest
{
    internal bool? IsEnabled { get; set; }
    internal string? ChangeReason { get; set; }
}
