using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Repository interface for managing MCP tool configuration audit trail.
/// Tracks all changes to tool configurations for compliance and debugging.
/// </summary>
public interface IToolConfigurationAuditRepository {
    /// <summary>
    /// Record a tool configuration change with audit details.
    /// </summary>
    /// <param name="toolConfigurationId">ID of the tool configuration being audited</param>
    /// <param name="toolId">Tool ID for easy tracking</param>
    /// <param name="action">Action performed (create, update, enable, disable, delete)</param>
    /// <param name="beforeJson">JSON snapshot before change (null for create)</param>
    /// <param name="afterJson">JSON snapshot after change</param>
    /// <param name="userId">User ID who made the change (null for system)</param>
    /// <param name="changeReason">Optional reason for the change</param>
    /// <returns>Created audit entry</returns>
    Task<ToolConfigurationAuditEntry> RecordChangeAsync(
        Guid toolConfigurationId,
        string toolId,
        string action,
        string? beforeJson,
        string? afterJson,
        string? userId,
        string? changeReason = null);

    /// <summary>
    /// Get audit history for a specific tool configuration.
    /// </summary>
    /// <param name="toolConfigurationId">Configuration ID to audit</param>
    /// <param name="limit">Maximum number of entries to return (default 100)</param>
    /// <param name="offset">Number of entries to skip for pagination</param>
    /// <returns>Array of audit entries ordered by date descending</returns>
    Task<ToolConfigurationAuditEntry[]> GetAuditHistoryAsync(
        Guid toolConfigurationId,
        int limit = 100,
        int offset = 0);

    /// <summary>
    /// Get audit entries by action type with optional date filtering.
    /// </summary>
    /// <param name="action">Action type to filter by</param>
    /// <param name="fromDate">Optional start date for filtering</param>
    /// <param name="toDate">Optional end date for filtering</param>
    /// <param name="limit">Maximum entries to return</param>
    /// <returns>Array of filtered audit entries</returns>
    Task<ToolConfigurationAuditEntry[]> GetAuditEntriesByActionAsync(
        string action,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int limit = 100);

    /// <summary>
    /// Get audit entries by user who made the change.
    /// </summary>
    /// <param name="userId">User ID to filter by</param>
    /// <param name="limit">Maximum entries to return</param>
    /// <param name="offset">Number of entries to skip for pagination</param>
    /// <returns>Array of user's audit entries</returns>
    Task<ToolConfigurationAuditEntry[]> GetAuditEntriesByUserAsync(
        string userId,
        int limit = 100,
        int offset = 0);

    /// <summary>
    /// Get audit summary statistics.
    /// </summary>
    /// <returns>Summary with counts and date ranges</returns>
    Task<ToolConfigurationAuditSummary> GetAuditSummaryAsync();

    /// <summary>
    /// Delete old audit entries based on retention policy.
    /// </summary>
    /// <param name="retentionDays">Number of days to keep (default 90)</param>
    /// <returns>Number of entries deleted</returns>
    Task<int> PurgeOldEntriesAsync(int retentionDays = 90);
}
