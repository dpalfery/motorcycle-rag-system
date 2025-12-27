using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Persistence.Sql.Repositories
{
    /// <summary>
    /// ADO.NET implementation of audit repository
    /// </summary>
    public class AuditRepository : IAuditRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<AuditRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the AuditRepository
        /// </summary>
        /// <param name="connectionFactory">SQL connection factory</param>
        /// <param name="logger">Logger</param>
        public AuditRepository(ISqlConnectionFactory connectionFactory, ILogger<AuditRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates an audit log entry
        /// </summary>
        /// <param name="auditLog">Audit log to create</param>
        /// <returns>Created audit log</returns>
        public async Task<AuditLog> CreateAuditLogAsync(AuditLog auditLog)
        {
            if (auditLog == null)
            {
                throw new ArgumentNullException(nameof(auditLog));
            }

            const string sql = @"
                INSERT INTO [dbo].[AuditLogs] (
                    [UserId], [UserEmail], [Action], [EntityType], [EntityId],
                    [OldValue], [NewValue], [ActionDate], [IpAddress], [UserAgent],
                    [Metadata], [Status], [ErrorMessage]
                )
                VALUES (
                    @UserId, @UserEmail, @Action, @EntityType, @EntityId,
                    @OldValue, @NewValue, @ActionDate, @IpAddress, @UserAgent,
                    @Metadata, @Status, @ErrorMessage
                );
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT) AS [Id];
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                using var transaction = connection.BeginTransaction();
                
                try
                {
                    var auditLogId = await connection.QueryFirstOrDefaultAsync<long>(sql, auditLog, transaction);
                    auditLog.Id = auditLogId;
                    
                    transaction.Commit();
                    _logger.LogDebug("Created audit log with ID {AuditLogId} for action {Action} on {EntityType}", 
                        auditLogId, auditLog.Action, auditLog.EntityType);
                    return auditLog;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create audit log");
                throw;
            }
        }

        /// <summary>
        /// Gets audit logs for a specific entity
        /// </summary>
        /// <param name="entityType">Entity type</param>
        /// <param name="entityId">Entity ID</param>
        /// <returns>List of audit logs for the entity</returns>
        public async Task<AuditLog[]> GetAuditLogsByEntityAsync(string entityType, string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityType))
            {
                throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));
            }

            if (string.IsNullOrWhiteSpace(entityId))
            {
                throw new ArgumentException("Entity ID cannot be null or empty", nameof(entityId));
            }

            const string sql = @"
                EXEC [dbo].[sp_GetAuditLogsByEntity]
                    @EntityType = @EntityType,
                    @EntityId = @EntityId;
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                return (await connection.QueryAsync<AuditLog>(sql, new 
                {
                    EntityType = entityType,
                    EntityId = entityId
                })).ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get audit logs for entity {EntityType} with ID {EntityId}", 
                    entityType, entityId);
                throw;
            }
        }

        /// <summary>
        /// Gets recent audit logs (limited count)
        /// </summary>
        /// <param name="limit">Maximum number of logs to return</param>
        /// <returns>List of recent audit logs</returns>
        public async Task<AuditLog[]> GetRecentAuditLogsAsync(int limit)
        {
            if (limit <= 0)
            {
                throw new ArgumentException("Limit must be greater than 0", nameof(limit));
            }

            const string sql = @"
                EXEC [dbo].[sp_GetRecentAuditLogs] @Limit = @Limit;
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                return (await connection.QueryAsync<AuditLog>(sql, new { Limit = limit })).ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get recent audit logs with limit {Limit}", limit);
                throw;
            }
        }
    }
}