using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Request DTO for creating a new MCP tool configuration.
/// Used in POST requests to create tool configurations.
/// </summary>
public class CreateMcpToolRequest {
    /// <summary>
    /// Unique identifier for the tool (e.g., 'web-search', 'pdf-analyzer').
    /// </summary>
    [Required(ErrorMessage = "Tool ID is required")]
    [StringLength(255)]
    public string ToolId { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the tool.
    /// </summary>
    [Required(ErrorMessage = "Name is required")]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of the tool and its purpose.
    /// </summary>
    [StringLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Server URL where the MCP tool is accessible.
    /// Must be HTTPS for production or HTTP on localhost for development.
    /// SSRF protection is enforced at validation.
    /// </summary>
    [Required(ErrorMessage = "Server URL is required")]
    [StringLength(500)]
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Tool type/category (e.g., 'search', 'analyzer', 'processor').
    /// </summary>
    [Required(ErrorMessage = "Tool Type is required")]
    [StringLength(100)]
    public string ToolType { get; set; } = string.Empty;

    /// <summary>
    /// Version number of the tool implementation.
    /// </summary>
    [StringLength(50)]
    public string? Version { get; set; }

    /// <summary>
    /// Whether the tool is enabled for use in orchestration.
    /// Default: true.
    /// </summary>
    public bool? IsEnabled { get; set; }

    /// <summary>
    /// Priority for tool selection during orchestration.
    /// Higher values take precedence.
    /// Default: 0.
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>
    /// Maximum time in milliseconds to wait for tool response.
    /// </summary>
    public int? TimeoutMs { get; set; }

    /// <summary>
    /// Whether to automatically retry if the tool call fails.
    /// Default: true.
    /// </summary>
    public bool? RetryOnFailure { get; set; }

    /// <summary>
    /// Maximum number of retry attempts on failure.
    /// Default: 3.
    /// </summary>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// Tool-specific configuration as JSON.
    /// Maximum 10KB. Must be valid JSON if provided.
    /// </summary>
    public string? ConfigurationJson { get; set; }
}

/// <summary>
/// Request DTO for updating an existing MCP tool configuration.
/// Used in PUT requests to update tool configurations.
/// All fields are optional - only provided fields are updated.
/// </summary>
public class UpdateMcpToolRequest {
    /// <summary>
    /// Updated display name (optional).
    /// </summary>
    [StringLength(255)]
    public string? Name { get; set; }

    /// <summary>
    /// Updated description (optional).
    /// </summary>
    [StringLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Updated server URL (optional).
    /// </summary>
    [StringLength(500)]
    public string? ServerUrl { get; set; }

    /// <summary>
    /// Updated tool type (optional).
    /// </summary>
    [StringLength(100)]
    public string? ToolType { get; set; }

    /// <summary>
    /// Updated enabled status (optional).
    /// </summary>
    public bool? IsEnabled { get; set; }

    /// <summary>
    /// Updated priority (optional).
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>
    /// Updated timeout in milliseconds (optional).
    /// </summary>
    public int? TimeoutMs { get; set; }

    /// <summary>
    /// Updated retry on failure setting (optional).
    /// </summary>
    public bool? RetryOnFailure { get; set; }

    /// <summary>
    /// Updated max retries (optional).
    /// </summary>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// Updated configuration JSON (optional).
    /// Must be valid JSON if provided. Maximum 10KB.
    /// </summary>
    public string? ConfigurationJson { get; set; }

    /// <summary>
    /// Reason for the change (for audit trail).
    /// </summary>
    public string? ChangeReason { get; set; }
}

/// <summary>
/// Request DTO for disabling an MCP tool.
/// </summary>
public class DisableMcpToolRequest {
    /// <summary>
    /// Reason for disabling the tool.
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Response DTO for MCP tool configuration.
/// Contains the complete configuration details for API responses.
/// SECURITY: Does NOT include internal auditing details.
/// </summary>
public class McpToolConfigurationDto {
    /// <summary>
    /// Unique internal identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Tool ID as specified by the tool provider.
    /// </summary>
    public string ToolId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the tool.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the tool.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Server URL where the tool is accessible.
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Whether the tool is enabled.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Type/category of the tool.
    /// </summary>
    public string ToolType { get; set; } = string.Empty;

    /// <summary>
    /// Version of the tool.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Whether this is a system-provided tool.
    /// </summary>
    public bool IsSystemTool { get; set; }

    /// <summary>
    /// Priority for selection during orchestration.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Timeout in milliseconds.
    /// </summary>
    public int? TimeoutMs { get; set; }

    /// <summary>
    /// Whether to retry on failure.
    /// </summary>
    public bool RetryOnFailure { get; set; }

    /// <summary>
    /// Maximum retry attempts.
    /// </summary>
    public int MaxRetries { get; set; }

    /// <summary>
    /// Reason the tool was disabled (if disabled).
    /// </summary>
    public string? DisabledReason { get; set; }

    /// <summary>
    /// Last known connection status.
    /// </summary>
    public string? LastConnectionStatus { get; set; }

    /// <summary>
    /// Timestamp of last successful connection test.
    /// </summary>
    public DateTime? LastTestedAt { get; set; }

    /// <summary>
    /// Tool-specific configuration as JSON.
    /// </summary>
    public string? ConfigurationJson { get; set; }

    /// <summary>
    /// When the configuration was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the configuration was last updated.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
