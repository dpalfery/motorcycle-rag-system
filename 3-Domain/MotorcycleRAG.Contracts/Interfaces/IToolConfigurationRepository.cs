using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Repository interface for managing MCP tool configurations.
/// Provides persistent storage and retrieval of tool configuration data.
/// </summary>
public interface IToolConfigurationRepository
{
    /// <summary>
    /// Add or update a tool configuration.
    /// </summary>
    /// <param name="configuration">Configuration to save</param>
    /// <returns>Saved configuration with database-assigned ID</returns>
    Task<McpToolConfiguration> AddOrUpdateAsync(McpToolConfiguration configuration);

    /// <summary>
    /// Get configuration by ID.
    /// </summary>
    /// <param name="id">Configuration ID</param>
    /// <returns>Configuration or null if not found</returns>
    Task<McpToolConfiguration?> GetByIdAsync(Guid id);

    /// <summary>
    /// Get configuration by tool ID (string identifier).
    /// </summary>
    /// <param name="toolId">Tool ID</param>
    /// <returns>Configuration or null if not found</returns>
    Task<McpToolConfiguration?> GetByToolIdAsync(string toolId);

    /// <summary>
    /// Get all configurations.
    /// </summary>
    /// <returns>Array of all configurations ordered by creation date</returns>
    Task<McpToolConfiguration[]> GetAllAsync();

    /// <summary>
    /// Get all enabled configurations.
    /// </summary>
    /// <returns>Array of enabled configurations ordered by priority</returns>
    Task<McpToolConfiguration[]> GetEnabledAsync();

    /// <summary>
    /// Get configurations by tool type.
    /// </summary>
    /// <param name="toolType">Tool type to filter by</param>
    /// <returns>Array of matching enabled configurations</returns>
    Task<McpToolConfiguration[]> GetByTypeAsync(string toolType);

    /// <summary>
    /// Delete a configuration by ID.
    /// </summary>
    /// <param name="id">Configuration ID to delete</param>
    /// <returns>True if deleted, false if not found</returns>
    Task<bool> DeleteAsync(Guid id);

    /// <summary>
    /// Check if a configuration exists by ID.
    /// </summary>
    /// <param name="id">Configuration ID</param>
    /// <returns>True if exists, false otherwise</returns>
    Task<bool> ExistsAsync(Guid id);

    /// <summary>
    /// Check if a tool ID already exists.
    /// </summary>
    /// <param name="toolId">Tool ID to check</param>
    /// <returns>True if exists, false otherwise</returns>
    Task<bool> ExistsByToolIdAsync(string toolId);

    /// <summary>
    /// Get count of all configurations.
    /// </summary>
    /// <returns>Total number of configurations</returns>
    Task<int> CountAsync();
}
