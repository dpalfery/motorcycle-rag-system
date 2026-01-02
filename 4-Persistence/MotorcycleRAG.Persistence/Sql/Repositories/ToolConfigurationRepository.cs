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
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        if (string.IsNullOrWhiteSpace(configuration.ToolId))
            throw new ArgumentException("Tool ID must not be empty", nameof(configuration));

        // Ensure ID is set
        if (configuration.Id == Guid.Empty)
            configuration.Id = Guid.NewGuid();

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
                configuration.ServerUrl,
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

            _logger.LogInformation("Tool configuration {ToolId} saved successfully", configuration.ToolId);
            return configuration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving tool configuration {ToolId}", configuration.ToolId);
            throw;
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

            var config = await connection.QueryFirstOrDefaultAsync<McpToolConfiguration>(
                sql,
                new { Id = id });

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tool configuration by ID {Id}", id);
            throw;
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

            var config = await connection.QueryFirstOrDefaultAsync<McpToolConfiguration>(
                sql,
                new { ToolId = toolId });

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tool configuration by ToolId {ToolId}", toolId);
            throw;
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

            var configs = await connection.QueryAsync<McpToolConfiguration>(sql);

            return configs.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all tool configurations");
            throw;
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

            var configs = await connection.QueryAsync<McpToolConfiguration>(sql);

            return configs.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving enabled tool configurations");
            throw;
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

            var configs = await connection.QueryAsync<McpToolConfiguration>(
                sql,
                new { ToolType = toolType });

            return configs.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tool configurations by type {ToolType}", toolType);
            throw;
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
            throw;
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
            throw;
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
            throw;
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
            throw;
        }
    }
}
