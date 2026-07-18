using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Persistence.Sql.Repositories;

/// <summary>
/// Repository for managing MCP tool configurations in SQL database.
/// Provides persistent CRUD operations for tool configuration data.
/// </summary>
public class ToolConfigurationRepository : IToolConfigurationRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<ToolConfigurationRepository> _logger;

    /// <summary>
    /// Dapper-friendly projection of the ToolConfigurations table. Materialized via
    /// <see cref="Map"/>, which routes through <see cref="McpToolConfiguration.Rehydrate"/>
    /// so the domain entity's invariants are enforced at the Persistence boundary.
    /// </summary>
    private sealed class McpToolConfigurationRow
    {
        public Guid Id { get; init; }
        public string ToolId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? ServerUrl { get; init; }
        public string ToolType { get; init; } = string.Empty;
        public string? Version { get; init; }
        public bool IsSystemTool { get; init; }
        public int Priority { get; init; }
        public int? TimeoutMs { get; init; }
        public bool RetryOnFailure { get; init; }
        public int MaxRetries { get; init; }
        public DateTime CreatedAt { get; init; }
        public bool IsEnabled { get; init; }
        public string? ConfigurationJson { get; init; }
        public string? DisabledReason { get; init; }
        public DateTime? LastTestedAt { get; init; }
        public string? LastConnectionStatus { get; init; }
        public DateTime? UpdatedAt { get; init; }
    }

    private static McpToolConfiguration? Map(McpToolConfigurationRow? row) =>
        row is null
            ? null
            : McpToolConfiguration.Rehydrate(
                id: row.Id,
                toolId: row.ToolId,
                name: row.Name,
                description: row.Description,
                serverUrl: MapServerUrl(row.ServerUrl),
                toolType: row.ToolType,
                version: row.Version,
                isSystemTool: row.IsSystemTool,
                priority: row.Priority,
                timeoutMs: row.TimeoutMs,
                retryOnFailure: row.RetryOnFailure,
                maxRetries: row.MaxRetries,
                createdAt: row.CreatedAt,
                isEnabled: row.IsEnabled,
                configurationJson: row.ConfigurationJson,
                disabledReason: row.DisabledReason,
                lastTestedAt: row.LastTestedAt,
                lastConnectionStatus: row.LastConnectionStatus,
                updatedAt: row.UpdatedAt);

    private static Uri? MapServerUrl(string? serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Persisted server URL must be an absolute URI.", nameof(serverUrl));
        }

        return uri;
    }

    public ToolConfigurationRepository(
        ISqlConnectionFactory connectionFactory,
        ILogger<ToolConfigurationRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Add or update a tool configuration.
    /// </summary>
    public async Task<McpToolConfiguration> AddOrUpdateAsync(McpToolConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.ToolId))
            throw new ArgumentException("Tool ID must not be empty", nameof(configuration));

        // Callers assign a persistence identity before invoking this method
        // (Id is init-only per the domain entity's invariant design).
        if (configuration.Id == Guid.Empty)
            throw new ArgumentException("Configuration Id must be assigned before persistence.", nameof(configuration));

        const string sql = @"
            MERGE INTO [dbo].[ToolConfigurations] target
            USING (SELECT @Id AS [Id]) source
            ON target.[Id] = source.[Id]
            WHEN MATCHED THEN
                UPDATE SET
                    [ToolId] = @ToolId,
                    [Name] = @Name,
                    [Description] = @Description,
                    [ServerUrl] = @ServerUrl,
                    [ToolType] = @ToolType,
                    [Version] = @Version,
                    [IsEnabled] = @IsEnabled,
                    [IsSystemTool] = @IsSystemTool,
                    [Priority] = @Priority,
                    [TimeoutMs] = @TimeoutMs,
                    [RetryOnFailure] = @RetryOnFailure,
                    [MaxRetries] = @MaxRetries,
                    [DisabledReason] = @DisabledReason,
                    [LastConnectionStatus] = @LastConnectionStatus,
                    [LastTestedAt] = @LastTestedAt,
                    [ConfigurationJson] = @ConfigurationJson,
                    [UpdatedAt] = @UpdatedAt
            WHEN NOT MATCHED THEN
                INSERT ([Id], [ToolId], [Name], [Description], [ServerUrl], [ToolType], [Version],
                        [IsEnabled], [IsSystemTool], [Priority], [TimeoutMs], [RetryOnFailure],
                        [MaxRetries], [DisabledReason], [LastConnectionStatus], [ConfigurationJson],
                        [CreatedAt], [UpdatedAt])
                VALUES (@Id, @ToolId, @Name, @Description, @ServerUrl, @ToolType, @Version,
                        @IsEnabled, @IsSystemTool, @Priority, @TimeoutMs, @RetryOnFailure,
                        @MaxRetries, @DisabledReason, @LastConnectionStatus, @ConfigurationJson,
                        @CreatedAt, @UpdatedAt);
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            await connection.ExecuteAsync(sql, new
            {
                configuration.Id,
                configuration.ToolId,
                configuration.Name,
                configuration.Description,
                ServerUrl = configuration.ServerUrl?.AbsoluteUri,
                configuration.ToolType,
                configuration.Version,
                configuration.IsEnabled,
                configuration.IsSystemTool,
                configuration.Priority,
                configuration.TimeoutMs,
                configuration.RetryOnFailure,
                configuration.MaxRetries,
                configuration.DisabledReason,
                configuration.LastConnectionStatus,
                configuration.ConfigurationJson,
                configuration.CreatedAt,
                configuration.UpdatedAt
            });

            _logger.LogInformation("Tool configuration {ToolId} saved successfully",
                configuration.ToolId);
            return configuration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving tool configuration {ToolId}",
                configuration.ToolId);
            throw new InvalidOperationException($"Error saving tool {nameof(configuration)} {configuration.ToolId}", ex);
        }
    }

    /// <summary>
    /// Get configuration by ID.
    /// </summary>
    public async Task<McpToolConfiguration?> GetByIdAsync(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("ID cannot be empty", nameof(id));

        const string sql = @"
            SELECT [Id], [ToolId], [Name], [Description], [ServerUrl], [ToolType], [Version],
                   [IsEnabled], [IsSystemTool], [Priority], [TimeoutMs], [RetryOnFailure],
                   [MaxRetries], [DisabledReason], [LastConnectionStatus], [LastTestedAt], [ConfigurationJson],
                   [CreatedAt], [UpdatedAt]
            FROM [dbo].[ToolConfigurations]
            WHERE [Id] = @Id;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var row = await connection.QueryFirstOrDefaultAsync<McpToolConfigurationRow>(
                sql,
                new { Id = id });

            return Map(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tool configuration by ID {Id}", id);
            throw new InvalidOperationException($"Error retrieving tool configuration by ID {id}", ex);
        }
    }

    /// <summary>
    /// Get configuration by tool ID (string identifier).
    /// </summary>
    public async Task<McpToolConfiguration?> GetByToolIdAsync(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId))
            return null;

        const string sql = @"
            SELECT [Id], [ToolId], [Name], [Description], [ServerUrl], [ToolType], [Version],
                   [IsEnabled], [IsSystemTool], [Priority], [TimeoutMs], [RetryOnFailure],
                   [MaxRetries], [DisabledReason], [LastConnectionStatus], [LastTestedAt], [ConfigurationJson],
                   [CreatedAt], [UpdatedAt]
            FROM [dbo].[ToolConfigurations]
            WHERE [ToolId] = @ToolId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var row = await connection.QueryFirstOrDefaultAsync<McpToolConfigurationRow>(
                sql,
                new { ToolId = toolId });

            return Map(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tool configuration by ToolId {ToolId}",
                toolId);
            throw new InvalidOperationException($"Error retrieving tool configuration by ToolId {toolId}", ex);
        }
    }

    /// <summary>
    /// Get all configurations.
    /// </summary>
    public async Task<McpToolConfiguration[]> GetAllAsync()
    {
        const string sql = @"
            SELECT [Id], [ToolId], [Name], [Description], [ServerUrl], [ToolType], [Version],
                   [IsEnabled], [IsSystemTool], [Priority], [TimeoutMs], [RetryOnFailure],
                   [MaxRetries], [DisabledReason], [LastConnectionStatus], [LastTestedAt], [ConfigurationJson],
                   [CreatedAt], [UpdatedAt]
            FROM [dbo].[ToolConfigurations]
            ORDER BY [CreatedAt];
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var rows = await connection.QueryAsync<McpToolConfigurationRow>(sql);

            return rows.Select(Map).ToArray()!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all tool configurations");
            throw new InvalidOperationException("Error retrieving all tool configurations", ex);
        }
    }

    /// <summary>
    /// Get all enabled configurations.
    /// </summary>
    public async Task<McpToolConfiguration[]> GetEnabledAsync()
    {
        const string sql = @"
            SELECT [Id], [ToolId], [Name], [Description], [ServerUrl], [ToolType], [Version],
                   [IsEnabled], [IsSystemTool], [Priority], [TimeoutMs], [RetryOnFailure],
                   [MaxRetries], [DisabledReason], [LastConnectionStatus], [LastTestedAt], [ConfigurationJson],
                   [CreatedAt], [UpdatedAt]
            FROM [dbo].[ToolConfigurations]
            WHERE [IsEnabled] = 1
            ORDER BY [Priority] DESC, [CreatedAt];
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var rows = await connection.QueryAsync<McpToolConfigurationRow>(sql);

            return rows.Select(Map).ToArray()!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving enabled tool configurations");
            throw new InvalidOperationException("Error retrieving enabled tool configurations", ex);
        }
    }

    /// <summary>
    /// Get configurations by tool type.
    /// </summary>
    public async Task<McpToolConfiguration[]> GetByTypeAsync(string toolType)
    {
        if (string.IsNullOrWhiteSpace(toolType))
            return Array.Empty<McpToolConfiguration>();

        const string sql = @"
            SELECT [Id], [ToolId], [Name], [Description], [ServerUrl], [ToolType], [Version],
                   [IsEnabled], [IsSystemTool], [Priority], [TimeoutMs], [RetryOnFailure],
                   [MaxRetries], [DisabledReason], [LastConnectionStatus], [LastTestedAt], [ConfigurationJson],
                   [CreatedAt], [UpdatedAt]
            FROM [dbo].[ToolConfigurations]
            WHERE [ToolType] = @ToolType AND [IsEnabled] = 1
            ORDER BY [Priority] DESC;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var rows = await connection.QueryAsync<McpToolConfigurationRow>(
                sql,
                new { ToolType = toolType });

            return rows.Select(Map).ToArray()!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tool configurations by type {ToolType}",
                toolType);
            throw new InvalidOperationException($"Error retrieving tool configurations by type {toolType}", ex);
        }
    }

    /// <summary>
    /// Delete a configuration by ID.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("ID cannot be empty", nameof(id));

        const string sql = @"
            DELETE FROM [dbo].[ToolConfigurations]
            WHERE [Id] = @Id;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });

            if (rowsAffected > 0)
                _logger.LogInformation("Tool configuration {Id} deleted successfully", id);

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting tool configuration {Id}", id);
            throw new InvalidOperationException($"Error deleting tool configuration {id}", ex);
        }
    }

    /// <summary>
    /// Check if a configuration exists by ID.
    /// </summary>
    public async Task<bool> ExistsAsync(Guid id)
    {
        if (id == Guid.Empty)
            return false;

        const string sql = "SELECT COUNT(1) FROM [dbo].[ToolConfigurations] WHERE [Id] = @Id;";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var count = await connection.QueryFirstOrDefaultAsync<int>(sql, new { Id = id });

            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking existence of tool configuration {Id}", id);
            throw new InvalidOperationException($"Error checking existence of tool configuration {id}", ex);
        }
    }

    /// <summary>
    /// Check if a tool ID already exists.
    /// </summary>
    public async Task<bool> ExistsByToolIdAsync(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId))
            return false;

        const string sql = "SELECT COUNT(1) FROM [dbo].[ToolConfigurations] WHERE [ToolId] = @ToolId;";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var count = await connection.QueryFirstOrDefaultAsync<int>(sql, new { ToolId = toolId });

            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking existence of tool ID {ToolId}", toolId);
            throw new InvalidOperationException($"Error checking existence of tool ID {toolId}", ex);
        }
    }

    /// <summary>
    /// Get count of all configurations.
    /// </summary>
    public async Task<int> CountAsync()
    {
        const string sql = "SELECT COUNT(1) FROM [dbo].[ToolConfigurations];";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();

            var count = await connection.QueryFirstOrDefaultAsync<int>(sql);

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting tool configurations");
            throw new InvalidOperationException("Error counting tool configurations", ex);
        }
    }
}
