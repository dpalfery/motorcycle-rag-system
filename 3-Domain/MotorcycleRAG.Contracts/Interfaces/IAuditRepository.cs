using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces {
    /// <summary>
    /// Repository interface for audit logging operations
    /// </summary>
    public interface IAuditRepository {
        /// <summary>
        /// Creates an audit log entry
        /// </summary>
        /// <param name="auditLog">Audit log to create</param>
        /// <returns>Created audit log</returns>
        Task<AuditLog> CreateAuditLogAsync(AuditLog auditLog);

        /// <summary>
        /// Gets audit logs for a specific entity
        /// </summary>
        /// <param name="entityType">Entity type</param>
        /// <param name="entityId">Entity ID</param>
        /// <returns>List of audit logs for the entity</returns>
        Task<AuditLog[]> GetAuditLogsByEntityAsync(string entityType, string entityId);

        /// <summary>
        /// Gets recent audit logs (limited count)
        /// </summary>
        /// <param name="limit">Maximum number of logs to return</param>
        /// <returns>List of recent audit logs</returns>
        Task<AuditLog[]> GetRecentAuditLogsAsync(int limit);
    }
}
