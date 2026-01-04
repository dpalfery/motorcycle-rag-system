using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Persistence.Sql.Repositories;

/// <summary>
/// Repository for MCP tool configuration audit trail
/// Tracks all changes to tool configurations for compliance and debugging
/// </summary>
public class ToolConfigurationAuditRepository : IToolConfigurationAuditRepository {
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<ToolConfigurationAuditRepository> _logger;

    public ToolConfigurationAuditRepository(
        ISqlConnectionFactory connectionFactory,
        ILogger<ToolConfigurationAuditRepository> logger) {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Record a tool configuration change
    /// </summary>
    public async Task<ToolConfigurationAuditEntry> RecordChangeAsync(
        Guid toolConfigurationId,
        string toolId,
        string action,
        string? beforeJson,
        string? afterJson,
        string? userId,
        string? changeReason = null) {
        if (toolConfigurationId == Guid.Empty)
            throw new ArgumentException("Tool configuration ID cannot be empty", nameof(toolConfigurationId));

        if (string.IsNullOrWhiteSpace(toolId))
            throw new ArgumentException("Tool ID cannot be empty", nameof(toolId));

        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action cannot be empty", nameof(action));

        const string sql = @"
            INSERT INTO [dbo].[ToolConfigurationAuditLog] (
                [ToolConfigurationId], [ToolId], [Action], [BeforeJson], [AfterJson],
                [UserId], [ChangeReason], [ChangedAt], [IpAddress]
            )
            VALUES (
                @ToolConfigurationId, @ToolId, @Action, @BeforeJson, @AfterJson,
                @UserId, @ChangeReason, @ChangedAt, NULL
            );
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT) AS [Id];
        ";

        try {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            using var transaction = connection.BeginTransaction();

            try {
                var auditId = await connection.QueryFirstOrDefaultAsync<long>(
                    sql,
                    new {
                        ToolConfigurationId = toolConfigurationId,
                        ToolId = toolId,
                        Action = action,
                        BeforeJson = beforeJson,
                        AfterJson = afterJson,
                        UserId = userId,
                        ChangeReason = changeReason,
                        ChangedAt = DateTime.UtcNow
                    },
                    transaction);

                transaction.Commit();

                var entry = new ToolConfigurationAuditEntry {
                    Id = auditId,
                    ToolConfigurationId = toolConfigurationId,
                    ToolId = toolId,
                    Action = action,
                    BeforeJson = beforeJson,
                    AfterJson = afterJson,
                    UserId = userId,
                    ChangeReason = changeReason,
                    ChangedAt = DateTime.UtcNow
                };

                _logger.LogInformation(
                    "Recorded audit entry for tool {ToolId}: {Action} by user {UserId}",
                    toolId, action, userId ?? "system");

                return entry;
            }
            catch {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex,
                "Error recording audit entry for tool {ToolId}: {Action}",
                toolId, action);
            throw;
        }
    }

    /// <summary>
    /// Get audit entries for a specific tool configuration
    /// </summary>
    public async Task<ToolConfigurationAuditEntry[]> GetAuditHistoryAsync(
        Guid toolConfigurationId,
        int limit = 100,
        int offset = 0) {
        if (toolConfigurationId == Guid.Empty)
            throw new ArgumentException("Tool configuration ID cannot be empty", nameof(toolConfigurationId));

        const string sql = @"
            SELECT [Id], [ToolConfigurationId], [ToolId], [Action], [BeforeJson], [AfterJson],
                   [UserId], [ChangeReason], [ChangedAt], [IpAddress]
            FROM [dbo].[ToolConfigurationAuditLog]
            WHERE [ToolConfigurationId] = @ToolConfigurationId
            ORDER BY [ChangedAt] DESC
            OFFSET @Offset ROWS
            FETCH NEXT @Limit ROWS ONLY;
        ";

        try {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var entries = await connection.QueryAsync<ToolConfigurationAuditEntry>(
                sql,
                new { ToolConfigurationId = toolConfigurationId, Limit = limit, Offset = offset });

            return entries.ToArray();
        }
        catch (Exception ex) {
            _logger.LogError(ex,
                "Error retrieving audit history for tool configuration {ToolConfigurationId}",
                toolConfigurationId);
            throw;
        }
    }

    /// <summary>
    /// Get audit entries by action type
    /// </summary>
    public async Task<ToolConfigurationAuditEntry[]> GetAuditEntriesByActionAsync(
        string action,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int limit = 100) {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action cannot be empty", nameof(action));

        const string sql = @"
            SELECT TOP (@Limit) [Id], [ToolConfigurationId], [ToolId], [Action], [BeforeJson], [AfterJson],
                   [UserId], [ChangeReason], [ChangedAt], [IpAddress]
            FROM [dbo].[ToolConfigurationAuditLog]
            WHERE [Action] = @Action
                AND (@FromDate IS NULL OR [ChangedAt] >= @FromDate)
                AND (@ToDate IS NULL OR [ChangedAt] <= @ToDate)
            ORDER BY [ChangedAt] DESC;
        ";

        var parameters = new DynamicParameters();
        parameters.Add("@Action", action);
        parameters.Add("@Limit", limit);
        parameters.Add("@FromDate", fromDate ?? (object?)DBNull.Value);
        parameters.Add("@ToDate", toDate ?? (object?)DBNull.Value);

        try {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var entries = await connection.QueryAsync<ToolConfigurationAuditEntry>(sql, parameters);

            return entries.ToArray();
        }
        catch (Exception ex) {
            _logger.LogError(ex,
                "Error retrieving audit entries for action {Action}",
                action);
            throw;
        }
    }

    /// <summary>
    /// Get audit entries by user
    /// </summary>
    public async Task<ToolConfigurationAuditEntry[]> GetAuditEntriesByUserAsync(
        string userId,
        int limit = 100,
        int offset = 0) {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be empty", nameof(userId));

        const string sql = @"
            SELECT [Id], [ToolConfigurationId], [ToolId], [Action], [BeforeJson], [AfterJson],
                   [UserId], [ChangeReason], [ChangedAt], [IpAddress]
            FROM [dbo].[ToolConfigurationAuditLog]
            WHERE [UserId] = @UserId
            ORDER BY [ChangedAt] DESC
            OFFSET @Offset ROWS
            FETCH NEXT @Limit ROWS ONLY;
        ";

        try {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var entries = await connection.QueryAsync<ToolConfigurationAuditEntry>(
                sql,
                new { UserId = userId, Limit = limit, Offset = offset });

            return entries.ToArray();
        }
        catch (Exception ex) {
            _logger.LogError(ex,
                "Error retrieving audit entries for user {UserId}",
                userId);
            throw;
        }
    }

    /// <summary>
    /// Get audit summary statistics
    /// </summary>
    public async Task<ToolConfigurationAuditSummary> GetAuditSummaryAsync() {
        const string sql = @"
            SELECT
                COUNT(*) AS TotalEntries,
                COUNT(DISTINCT [ToolConfigurationId]) AS UniqueTools,
                COUNT(DISTINCT [UserId]) AS UniqueUsers,
                MIN([ChangedAt]) AS OldestEntry,
                MAX([ChangedAt]) AS LatestEntry
            FROM [dbo].[ToolConfigurationAuditLog];
        ";

        try {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var summary = await connection.QueryFirstOrDefaultAsync<ToolConfigurationAuditSummary>(sql);

            return summary ?? new ToolConfigurationAuditSummary();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving audit summary");
            throw;
        }
    }

    /// <summary>
    /// Clear old audit entries (retention policy)
    /// </summary>
    public async Task<int> PurgeOldEntriesAsync(int retentionDays = 90) {
        const string sql = @"
            DELETE FROM [dbo].[ToolConfigurationAuditLog]
            WHERE [ChangedAt] < DATEADD(DAY, -@RetentionDays, GETUTCDATE());
        ";

        try {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var deletedCount = await connection.ExecuteAsync(
                sql,
                new { RetentionDays = retentionDays });

            _logger.LogInformation(
                "Purged {Count} audit entries older than {Days} days",
                deletedCount, retentionDays);

            return deletedCount;
        }
        catch (Exception ex) {
            _logger.LogError(ex,
                "Error purging old audit entries");
            throw;
        }
    }
}
