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
public class McpToolConfiguration {
    private bool _isEnabled = true;
    private string? _disabledReason;
    private string? _configurationJson;
    private DateTime? _lastTestedAt;
    private string? _lastConnectionStatus;
    private DateTime? _updatedAt;

    [Required]
    public Guid Id { get; init; }

    /// <summary>
    /// Unique identifier for the MCP tool
    /// </summary>
    [Required]
    [StringLength(255)]
    public string ToolId { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable name of the tool
    /// </summary>
    [Required]
    [StringLength(255)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Description of what the tool does
    /// </summary>
    [StringLength(1000)]
    public string? Description { get; init; }

    /// <summary>
    /// The MCP server URL or connection string
    /// </summary>
    [Required]
    public Uri ServerUrl { get; init; } = new Uri("about:blank");

    /// <summary>
    /// Whether this tool is enabled for use in orchestration
    /// </summary>
    public bool IsEnabled { get => _isEnabled; init => _isEnabled = value; }

    /// <summary>
    /// Tool configuration in JSON format
    /// Contains tool-specific settings and parameters
    /// </summary>
    public string? ConfigurationJson { get => _configurationJson; init => _configurationJson = value; }

    /// <summary>
    /// The type of tool (e.g., "search", "processor", "validator")
    /// </summary>
    [Required]
    [StringLength(100)]
    public string ToolType { get; init; } = string.Empty;

    /// <summary>
    /// Version of the tool configuration
    /// </summary>
    [StringLength(50)]
    public string? Version { get; init; }

    /// <summary>
    /// Whether the tool is a built-in system tool
    /// </summary>
    public bool IsSystemTool { get; init; }

    /// <summary>
    /// Priority for orchestration (higher = more preferred)
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Connection timeout in milliseconds
    /// </summary>
    public int? TimeoutMs { get; init; } = 30000;

    /// <summary>
    /// Whether to retry on failure
    /// </summary>
    public bool RetryOnFailure { get; init; } = true;

    /// <summary>
    /// Maximum number of retries
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Reason for disabling if IsEnabled is false
    /// </summary>
    [StringLength(500)]
    public string? DisabledReason { get => _disabledReason; init => _disabledReason = value; }

    /// <summary>
    /// Last tested timestamp
    /// </summary>
    public DateTime? LastTestedAt { get => _lastTestedAt; init => _lastTestedAt = value; }

    /// <summary>
    /// Last connection status
    /// </summary>
    [StringLength(50)]
    public string? LastConnectionStatus { get => _lastConnectionStatus; init => _lastConnectionStatus = value; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get => _updatedAt; init => _updatedAt = value; }

    /// <summary>
    /// Creates a validated tool configuration for application code. The parameterless
    /// initializer remains available for persistence hydration.
    /// </summary>
    public static McpToolConfiguration Create(
        string toolId,
        string name,
        Uri serverUrl,
        string toolType,
        int? timeoutMs = 30000,
        int maxRetries = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolType);
        if (!serverUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("Server URL must be absolute.", nameof(serverUrl));
        }

        if (timeoutMs is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), timeoutMs, "Timeout must be positive.");
        }

        if (maxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetries), maxRetries, "Max retries cannot be negative.");
        }

        return new McpToolConfiguration
        {
            Id = Guid.NewGuid(),
            ToolId = toolId.Trim(),
            Name = name.Trim(),
            ServerUrl = serverUrl,
            ToolType = toolType.Trim(),
            TimeoutMs = timeoutMs,
            MaxRetries = maxRetries
        };
    }

    // Domain behavior
    public void Enable() {
        _isEnabled = true;
        _disabledReason = null;
        _updatedAt = DateTime.UtcNow;
    }

    public void Disable(string reason) {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _isEnabled = false;
        _disabledReason = reason.Trim();
        _updatedAt = DateTime.UtcNow;
    }

    public void UpdateConfiguration(string? configurationJson) {
        _configurationJson = configurationJson;
        _updatedAt = DateTime.UtcNow;
    }

    public void UpdateConnectionStatus(string status) {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        _lastConnectionStatus = status.Trim();
        _lastTestedAt = DateTime.UtcNow;
        _updatedAt = DateTime.UtcNow;
    }
}
