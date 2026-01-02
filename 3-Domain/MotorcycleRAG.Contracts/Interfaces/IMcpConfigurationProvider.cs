using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Provides MCP (Model Context Protocol) tool configuration with refresh behavior.
/// Interfaces between application logic and tool configuration storage.
/// This is a transitional service during migration from in-memory to database-backed storage.
/// </summary>
public interface IMcpConfigurationProvider : IDisposable
{
    /// <summary>
    /// Get all enabled MCP tool configurations
    /// </summary>
    Task<McpToolConfiguration[]> GetEnabledToolsAsync();

    /// <summary>
    /// Get configuration by tool ID
    /// </summary>
    Task<McpToolConfiguration?> GetToolConfigurationAsync(string toolId);

    /// <summary>
    /// Get configurations by type (e.g., "search", "processor")
    /// </summary>
    Task<McpToolConfiguration[]> GetToolsByTypeAsync(string toolType);

    /// <summary>
    /// Refresh configurations from store
    /// </summary>
    Task RefreshAsync();

    /// <summary>
    /// Get last refresh time
    /// </summary>
    DateTime LastRefreshed { get; }

    /// <summary>
    /// Check if configuration is valid and accessible
    /// </summary>
    Task<bool> ValidateToolAsync(McpToolConfiguration tool);
}
