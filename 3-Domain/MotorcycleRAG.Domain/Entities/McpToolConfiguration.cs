// <copyright file="McpToolConfiguration.cs" company="MotorcycleRAG">
// Copyright (c) MotorcycleRAG. All rights reserved.
// </copyright>

using System;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// MCP (Model Context Protocol) Tool Configuration
/// Defines which MCP tools are enabled/disabled and their settings
/// </summary>
public class McpToolConfiguration
{
    [Required]
    public Guid Id { get; set; }

    /// <summary>
    /// Unique identifier for the MCP tool
    /// </summary>
    [Required]
    [StringLength(255)]
    public string ToolId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name of the tool
    /// </summary>
    [Required]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what the tool does
    /// </summary>
    [StringLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// The MCP server URL or connection string
    /// </summary>
    [Required]
    public Uri ServerUrl { get; set; } = new("about:blank");

    /// <summary>
    /// Whether this tool is enabled for use in orchestration
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Tool configuration in JSON format
    /// Contains tool-specific settings and parameters
    /// </summary>
    public string? ConfigurationJson { get; set; }

    /// <summary>
    /// The type of tool (e.g., "search", "processor", "validator")
    /// </summary>
    [Required]
    [StringLength(100)]
    public string ToolType { get; set; } = string.Empty;

    /// <summary>
    /// Version of the tool configuration
    /// </summary>
    [StringLength(50)]
    public string? Version { get; set; }

    /// <summary>
    /// Whether the tool is a built-in system tool
    /// </summary>
    public bool IsSystemTool { get; set; }

    /// <summary>
    /// Priority for orchestration (higher = more preferred)
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Connection timeout in milliseconds
    /// </summary>
    public int? TimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Whether to retry on failure
    /// </summary>
    public bool RetryOnFailure { get; set; } = true;

    /// <summary>
    /// Maximum number of retries
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Reason for disabling if IsEnabled is false
    /// </summary>
    [StringLength(500)]
    public string? DisabledReason { get; set; }

    /// <summary>
    /// Last tested timestamp
    /// </summary>
    public DateTime? LastTestedAt { get; set; }

    /// <summary>
    /// Last connection status
    /// </summary>
    [StringLength(50)]
    public string? LastConnectionStatus { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Domain behavior
    public void Enable()
    {
        IsEnabled = true;
        DisabledReason = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Disable(string reason)
    {
        IsEnabled = false;
        DisabledReason = reason;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateConfiguration(string? configurationJson)
    {
        ConfigurationJson = configurationJson;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateConnectionStatus(string status)
    {
        LastConnectionStatus = status;
        LastTestedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
