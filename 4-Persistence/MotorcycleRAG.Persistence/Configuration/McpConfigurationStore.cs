using Microsoft.Extensions.Logging;
using MotorcycleRAG.Domain.Entities;
using System.Collections.Concurrent;

namespace MotorcycleRAG.Persistence.Configuration;

/// <summary>
/// In-memory store for MCP (Model Context Protocol) tool configurations
/// Provides thread-safe access to MCP tool settings with change tracking
/// </summary>
public class McpConfigurationStore
{
    private readonly ILogger<McpConfigurationStore> _logger;
    private readonly ConcurrentDictionary<Guid, McpToolConfiguration> _configurations;
    private readonly ConcurrentDictionary<string, Guid> _toolIdLookup;
    private DateTime _lastUpdated;

    // Event for configuration change notifications
    public event EventHandler<McpConfigurationChangedEventArgs>? ConfigurationChanged;

    public McpConfigurationStore(ILogger<McpConfigurationStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configurations = new ConcurrentDictionary<Guid, McpToolConfiguration>();
        _toolIdLookup = new ConcurrentDictionary<string, Guid>();
        _lastUpdated = DateTime.UtcNow;

        _logger.LogInformation("MCP Configuration store initialized");
    }

    /// <summary>
    /// Get last update timestamp
    /// </summary>
    public DateTime LastUpdated => _lastUpdated;

    /// <summary>
    /// Add or update a tool configuration
    /// </summary>
    public void AddOrUpdateConfiguration(McpToolConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.ToolId))
            throw new ArgumentException("ToolId must not be empty", nameof(configuration));

        // Callers assign a persistence identity before storing (Id is init-only per
        // the domain entity's invariant design); the store no longer generates one.
        if (configuration.Id == Guid.Empty)
            throw new ArgumentException("Configuration Id must be assigned before storing.", nameof(configuration));

        var id = configuration.Id;
        _lastUpdated = DateTime.UtcNow;

        var wasNew = !_configurations.ContainsKey(id);
        _configurations.AddOrUpdate(id, configuration, (_, _) => configuration);

        // Update lookup
        _toolIdLookup.AddOrUpdate(configuration.ToolId, id, (_, _) => id);

        _logger.LogInformation("MCP tool configuration '{ToolId}' ({ToolName}) {Action}",
            configuration.ToolId, configuration.Name, wasNew ? "added" : "updated");

        // Raise change event
        ConfigurationChanged?.Invoke(this, new McpConfigurationChangedEventArgs
        {
            ConfigurationId = id,
            ToolId = configuration.ToolId,
            ChangeType = wasNew ? "added" : "updated",
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Get configuration by ID
    /// </summary>
    public McpToolConfiguration? GetConfigurationById(Guid id)
    {
        _configurations.TryGetValue(id, out var config);
        return config;
    }

    /// <summary>
    /// Get configuration by tool ID
    /// </summary>
    public McpToolConfiguration? GetConfigurationByToolId(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId))
            return null;

        if (_toolIdLookup.TryGetValue(toolId, out var id))
        {
            return GetConfigurationById(id);
        }

        return null;
    }

    /// <summary>
    /// Get all configurations
    /// </summary>
    public McpToolConfiguration[] GetAllConfigurations()
    {
        return _configurations.Values.OrderBy(c => c.CreatedAt).ToArray();
    }

    /// <summary>
    /// Get all enabled configurations
    /// </summary>
    public McpToolConfiguration[] GetEnabledConfigurations()
    {
        return _configurations.Values
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.Priority)
            .ThenBy(c => c.CreatedAt)
            .ToArray();
    }

    /// <summary>
    /// Get configurations by type
    /// </summary>
    public McpToolConfiguration[] GetConfigurationsByType(string toolType)
    {
        if (string.IsNullOrWhiteSpace(toolType))
            return Array.Empty<McpToolConfiguration>();

        return _configurations.Values
            .Where(c => c.ToolType == toolType && c.IsEnabled)
            .OrderBy(c => c.Priority)
            .ToArray();
    }

    /// <summary>
    /// Remove configuration by ID
    /// </summary>
    public bool RemoveConfiguration(Guid id)
    {
        if (_configurations.TryRemove(id, out var removed))
        {
            _toolIdLookup.TryRemove(removed.ToolId, out _);
            _lastUpdated = DateTime.UtcNow;

            _logger.LogInformation("Removed MCP tool configuration '{ToolId}'", removed.ToolId);

            ConfigurationChanged?.Invoke(this, new McpConfigurationChangedEventArgs
            {
                ConfigurationId = id,
                ToolId = removed.ToolId,
                ChangeType = "removed",
                Timestamp = DateTime.UtcNow
            });

            return true;
        }

        return false;
    }

    /// <summary>
    /// Clear all configurations
    /// </summary>
    public void Clear()
    {
        _configurations.Clear();
        _toolIdLookup.Clear();
        _lastUpdated = DateTime.UtcNow;

        _logger.LogWarning("Cleared all MCP tool configurations");

        ConfigurationChanged?.Invoke(this, new McpConfigurationChangedEventArgs
        {
            ChangeType = "cleared",
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Get count of configurations
    /// </summary>
    public int Count => _configurations.Count;

    /// <summary>
    /// Check if configuration exists
    /// </summary>
    public bool Contains(Guid id) => _configurations.ContainsKey(id);

    /// <summary>
    /// Check if tool ID exists
    /// </summary>
    public bool ContainsTool(string toolId) => _toolIdLookup.ContainsKey(toolId);
}

/// <summary>
/// Event arguments for MCP configuration changes
/// </summary>
public class McpConfigurationChangedEventArgs : EventArgs
{
    public Guid ConfigurationId { get; set; }
    public string? ToolId { get; set; }
    public string ChangeType { get; set; } = string.Empty; // "added", "updated", "removed", "cleared"
    public DateTime Timestamp { get; set; }
}
