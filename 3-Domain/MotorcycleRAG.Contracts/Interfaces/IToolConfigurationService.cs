using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Application service for managing MCP tool configurations.
/// Provides orchestration between the Presentation layer and data persistence layer.
/// This is the boundary that the API layer should depend on, following Clean Architecture.
/// </summary>
public interface IToolConfigurationService
{
    /// <summary>
    /// Get all tool configurations.
    /// </summary>
    /// <returns>Array of all configurations</returns>
    Task<McpToolConfiguration[]> GetAllToolsAsync();

    /// <summary>
    /// Get a specific tool configuration by tool ID.
    /// </summary>
    /// <param name="toolId">Tool ID to retrieve</param>
    /// <returns>Configuration or null if not found</returns>
    Task<McpToolConfiguration?> GetToolAsync(string toolId);

    /// <summary>
    /// Get all enabled tools for orchestration.
    /// </summary>
    /// <returns>Array of enabled configurations ordered by priority</returns>
    Task<McpToolConfiguration[]> GetEnabledToolsAsync();

    /// <summary>
    /// Create a new MCP tool configuration.
    /// </summary>
    /// <param name="configuration">Configuration to create</param>
    /// <param name="userId">User ID performing the action (for audit)</param>
    /// <returns>Created configuration with assigned ID</returns>
    Task<McpToolConfiguration> CreateToolAsync(McpToolConfiguration configuration, string? userId = null);

    /// <summary>
    /// Update an existing MCP tool configuration.
    /// </summary>
    /// <param name="toolId">Tool ID to update</param>
    /// <param name="configuration">Updated configuration</param>
    /// <param name="changeReason">Reason for the change (for audit)</param>
    /// <param name="userId">User ID performing the action (for audit)</param>
    /// <returns>Updated configuration</returns>
    Task<McpToolConfiguration> UpdateToolAsync(
        string toolId,
        McpToolConfiguration configuration,
        string? changeReason = null,
        string? userId = null);

    /// <summary>
    /// Enable a tool configuration.
    /// </summary>
    /// <param name="toolId">Tool ID to enable</param>
    /// <param name="userId">User ID performing the action (for audit)</param>
    /// <returns>Updated configuration</returns>
    Task<McpToolConfiguration> EnableToolAsync(string toolId, string? userId = null);

    /// <summary>
    /// Disable a tool configuration.
    /// </summary>
    /// <param name="toolId">Tool ID to disable</param>
    /// <param name="reason">Reason for disabling (for audit)</param>
    /// <param name="userId">User ID performing the action (for audit)</param>
    /// <returns>Updated configuration</returns>
    Task<McpToolConfiguration> DisableToolAsync(string toolId, string reason, string? userId = null);

    /// <summary>
    /// Validate a tool configuration is accessible and correctly configured.
    /// </summary>
    /// <param name="tool">Configuration to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    Task<bool> ValidateToolAsync(McpToolConfiguration tool);

    /// <summary>
    /// Delete a tool configuration.
    /// </summary>
    /// <param name="toolId">Tool ID to delete</param>
    /// <param name="userId">User ID performing the action (for audit)</param>
    /// <returns>True if deleted, false if not found</returns>
    Task<bool> DeleteToolAsync(string toolId, string? userId = null);

    /// <summary>
    /// Get audit history for a specific tool configuration.
    /// </summary>
    /// <param name="configId">Configuration ID</param>
    /// <param name="limit">Maximum number of audit entries to return</param>
    /// <returns>Array of audit entries</returns>
    Task<ToolConfigurationAuditEntry[]> GetAuditHistoryAsync(Guid configId, int limit = 100);

    /// <summary>
    /// Get audit summary statistics.
    /// </summary>
    /// <returns>Audit summary with total counts and date range</returns>
    Task<ToolConfigurationAuditSummary> GetAuditSummaryAsync();

    /// <summary>
    /// Get audit entries filtered by action type.
    /// </summary>
    /// <param name="action">Action type to filter by</param>
    /// <returns>Array of audit entries matching the action</returns>
    Task<ToolConfigurationAuditEntry[]> GetAuditEntriesByActionAsync(string action);
}
