namespace MotorcycleRAG.Admin.Services.Dtos;

/// <summary>
/// DTO for MCP tool configuration (from API)
/// </summary>
internal class McpToolConfigurationDto
{
    internal Guid Id { get; set; }
    internal string ToolId { get; set; } = string.Empty;
    internal string Name { get; set; } = string.Empty;
    internal string? Description { get; set; }
    internal string ServerUrl { get; set; } = string.Empty;
    internal bool IsEnabled { get; set; }
    internal string ToolType { get; set; } = string.Empty;
    internal string? Version { get; set; }
    internal bool IsSystemTool { get; set; }
    internal int Priority { get; set; }
    internal int? TimeoutMs { get; set; }
    internal bool RetryOnFailure { get; set; }
    internal int MaxRetries { get; set; }
    internal string? DisabledReason { get; set; }
    internal string? LastConnectionStatus { get; set; }
    internal DateTime? LastTestedAt { get; set; }
    /// <summary>
    /// Tool-specific configuration as JSON (max 10KB)
    /// </summary>
    internal string? ConfigurationJson { get; set; }
    internal DateTime CreatedAt { get; set; }
    internal DateTime? UpdatedAt { get; set; }
}
