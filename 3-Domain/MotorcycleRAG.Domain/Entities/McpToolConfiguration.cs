// <copyright file="McpToolConfiguration.cs" company="MotorcycleRAG">
// Copyright (c) MotorcycleRAG. All rights reserved.
// </copyright>

using System;

namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// MCP (Model Context Protocol) Tool Configuration.
/// Defines which MCP tools are enabled/disabled and their settings.
/// </summary>
/// <remarks>
/// Construction is restricted to the <see cref="Create"/> and <see cref="Rehydrate"/>
/// factories. Identity fields are immutable after construction; lifecycle fields are
/// mutated only through the named domain transitions (<see cref="Enable"/>,
/// <see cref="Disable"/>, <see cref="UpdateConfiguration"/>, <see cref="UpdateConnectionStatus"/>).
/// </remarks>
public class McpToolConfiguration
{
    /// <summary>Stable persistence identity assigned once at creation.</summary>
    public Guid Id { get; }

    /// <summary>Unique identifier for the MCP tool.</summary>
    public string ToolId { get; }

    /// <summary>Human-readable name of the tool.</summary>
    public string Name { get; }

    /// <summary>Description of what the tool does.</summary>
    public string? Description { get; }

    /// <summary>The MCP server URL or connection string.</summary>
    public Uri? ServerUrl { get; }

    /// <summary>The type of tool (e.g., "search", "processor", "validator").</summary>
    public string ToolType { get; }

    /// <summary>Version of the tool configuration.</summary>
    public string? Version { get; }

    /// <summary>Whether the tool is a built-in system tool.</summary>
    public bool IsSystemTool { get; }

    /// <summary>Priority for orchestration (higher = more preferred).</summary>
    public int Priority { get; }

    /// <summary>Connection timeout in milliseconds.</summary>
    public int? TimeoutMs { get; }

    /// <summary>Whether to retry on failure.</summary>
    public bool RetryOnFailure { get; }

    /// <summary>Maximum number of retries.</summary>
    public int MaxRetries { get; }

    /// <summary>Timestamp the configuration was first persisted.</summary>
    public DateTime CreatedAt { get; }

    /// <summary>Whether this tool is enabled for use in orchestration.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// Tool configuration in JSON format. Contains tool-specific settings and parameters.
    /// </summary>
    public string? ConfigurationJson { get; private set; }

    /// <summary>Reason for disabling if <see cref="IsEnabled"/> is false.</summary>
    public string? DisabledReason { get; private set; }

    /// <summary>Last tested timestamp.</summary>
    public DateTime? LastTestedAt { get; private set; }

    /// <summary>Last connection status.</summary>
    public string? LastConnectionStatus { get; private set; }

    /// <summary>Timestamp of the most recent mutation.</summary>
    public DateTime? UpdatedAt { get; private set; }

    private McpToolConfiguration(
        Guid id,
        string toolId,
        string name,
        string? description,
        Uri? serverUrl,
        string toolType,
        string? version,
        bool isSystemTool,
        int priority,
        int? timeoutMs,
        bool retryOnFailure,
        int maxRetries,
        DateTime createdAt,
        bool isEnabled,
        string? configurationJson,
        string? disabledReason,
        DateTime? lastTestedAt,
        string? lastConnectionStatus,
        DateTime? updatedAt)
    {
        Id = id;
        ToolId = toolId;
        Name = name;
        Description = description;
        ServerUrl = serverUrl;
        ToolType = toolType;
        Version = version;
        IsSystemTool = isSystemTool;
        Priority = priority;
        TimeoutMs = timeoutMs;
        RetryOnFailure = retryOnFailure;
        MaxRetries = maxRetries;
        CreatedAt = createdAt;
        IsEnabled = isEnabled;
        ConfigurationJson = configurationJson;
        DisabledReason = disabledReason;
        LastTestedAt = lastTestedAt;
        LastConnectionStatus = lastConnectionStatus;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Creates a validated tool configuration for application code with sensible defaults.
    /// New configurations start enabled with no audit/transition state populated.
    /// </summary>
    public static McpToolConfiguration Create(
        string toolId,
        string name,
        Uri serverUrl,
        string toolType,
        int? timeoutMs = 30000,
        int maxRetries = 3) =>
        Rehydrate(
            id: Guid.NewGuid(),
            toolId: toolId,
            name: name,
            description: null,
            serverUrl: serverUrl,
            toolType: toolType,
            version: null,
            isSystemTool: false,
            priority: 0,
            timeoutMs: timeoutMs,
            retryOnFailure: true,
            maxRetries: maxRetries,
            createdAt: DateTime.UtcNow,
            isEnabled: true,
            configurationJson: null,
            disabledReason: null,
            lastTestedAt: null,
            lastConnectionStatus: null,
            updatedAt: null);

    /// <summary>
    /// Rehydrates a persisted tool configuration after validating identity at the
    /// Persistence boundary. Invalid database rows are rejected rather than becoming
    /// a partially-valid domain entity. Transition state is taken verbatim so callers
    /// such as <see cref="Application.Services.ToolConfigurationService"/> can rebuild
    /// an immutable snapshot for write-back via the named domain transitions.
    /// </summary>
    public static McpToolConfiguration Rehydrate(
        Guid id,
        string toolId,
        string name,
        string? description,
        Uri? serverUrl,
        string toolType,
        string? version,
        bool isSystemTool,
        int priority,
        int? timeoutMs,
        bool retryOnFailure,
        int maxRetries,
        DateTime createdAt,
        bool isEnabled,
        string? configurationJson,
        string? disabledReason,
        DateTime? lastTestedAt,
        string? lastConnectionStatus,
        DateTime? updatedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Configuration id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(toolId))
        {
            throw new ArgumentException("Tool id is required.", nameof(toolId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tool name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(toolType))
        {
            throw new ArgumentException("Tool type is required.", nameof(toolType));
        }

        return new McpToolConfiguration(
            id,
            toolId.Trim(),
            name.Trim(),
            description,
            serverUrl,
            toolType.Trim(),
            version,
            isSystemTool,
            priority,
            timeoutMs,
            retryOnFailure,
            maxRetries,
            createdAt,
            isEnabled,
            configurationJson,
            disabledReason,
            lastTestedAt,
            lastConnectionStatus,
            updatedAt);
    }

    // Domain behavior

    /// <summary>Enables the tool and clears any prior disabled reason.</summary>
    public void Enable()
    {
        IsEnabled = true;
        DisabledReason = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Disables the tool with a non-empty reason.</summary>
    public void Disable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        IsEnabled = false;
        DisabledReason = reason.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Replaces the tool-specific JSON configuration.</summary>
    public void UpdateConfiguration(string? configurationJson)
    {
        ConfigurationJson = configurationJson;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Records the latest connection probe result.</summary>
    public void UpdateConnectionStatus(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        LastConnectionStatus = status.Trim();
        LastTestedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
