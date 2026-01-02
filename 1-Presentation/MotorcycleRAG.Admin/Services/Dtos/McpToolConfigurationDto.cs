namespace MotorcycleRAG.Admin.Services.Dtos;

/// <summary>
/// DTO for MCP tool configuration (from API)
/// </summary>
public class McpToolConfigurationDto
{
    public Guid Id { get; set; }
    public string ToolId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ServerUrl { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string ToolType { get; set; } = string.Empty;
    public string? Version { get; set; }
    public bool IsSystemTool { get; set; }
    public int Priority { get; set; }
    public int? TimeoutMs { get; set; }
    public bool RetryOnFailure { get; set; }
    public int MaxRetries { get; set; }
    public string? DisabledReason { get; set; }
    public string? LastConnectionStatus { get; set; }
    public DateTime? LastTestedAt { get; set; }
    /// <summary>
    /// Tool-specific configuration as JSON (max 10KB)
    /// </summary>
    public string? ConfigurationJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
