using System;
using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Service interface for application-level audit logging.
/// Tracks security-relevant events including authentication, configuration changes, and data access.
/// Implements OWASP ASVS Level 2 logging and monitoring requirements.
/// </summary>
public interface IAuditService {
    /// <summary>
    /// Logs a user authentication event (login).
    /// </summary>
    /// <param name="userId">User ID (will be sanitized/hashed)</param>
    /// <param name="email">User email (optional, for traceability)</param>
    /// <param name="ipAddress">IP address of the client (optional)</param>
    /// <param name="userAgent">User agent string (optional)</param>
    /// <returns>Audit log entry</returns>
    Task<AuditLog> LogAuthenticationAsync(
        string userId,
        string? email = null,
        string? ipAddress = null,
        string? userAgent = null);

    /// <summary>
    /// Logs a user logout event.
    /// </summary>
    /// <param name="userId">User ID (will be sanitized/hashed)</param>
    /// <param name="ipAddress">IP address of the client (optional)</param>
    /// <returns>Audit log entry</returns>
    Task<AuditLog> LogLogoutAsync(
        string userId,
        string? ipAddress = null);

    /// <summary>
    /// Logs a failed authentication attempt.
    /// </summary>
    /// <param name="email">Email attempted (optional, redacted in logs)</param>
    /// <param name="reason">Reason for failure (e.g., "Invalid password", "Account locked")</param>
    /// <param name="ipAddress">IP address of the client (optional)</param>
    /// <param name="userAgent">User agent string (optional)</param>
    /// <returns>Audit log entry</returns>
    Task<AuditLog> LogFailedAuthenticationAsync(
        string? email,
        string reason,
        string? ipAddress = null,
        string? userAgent = null);

    /// <summary>
    /// Logs a configuration change (MCP tools, web sources, etc.).
    /// </summary>
    /// <param name="userId">User ID who made the change (will be sanitized/hashed)</param>
    /// <param name="entityType">Type of entity changed (e.g., "McpTool", "WebSource")</param>
    /// <param name="entityId">ID of the entity changed</param>
    /// <param name="action">Action performed (create, update, delete, enable, disable)</param>
    /// <param name="beforeValue">Previous value (optional, for updates)</param>
    /// <param name="afterValue">New value (optional, for updates)</param>
    /// <param name="changeReason">Reason for the change (optional)</param>
    /// <returns>Audit log entry</returns>
    Task<AuditLog> LogConfigurationChangeAsync(
        string userId,
        string entityType,
        string entityId,
        string action,
        string? beforeValue = null,
        string? afterValue = null,
        string? changeReason = null);

    /// <summary>
    /// Logs a data access operation (ingestion, search, deletion).
    /// </summary>
    /// <param name="userId">User ID who performed the operation (will be sanitized/hashed)</param>
    /// <param name="entityType">Type of data accessed (e.g., "Document", "MotorcycleSpec", "WebPage")</param>
    /// <param name="entityId">ID of the entity accessed</param>
    /// <param name="action">Action performed (read, ingest, delete, index)</param>
    /// <param name="status">Status of the operation (Success, Failure)</param>
    /// <param name="errorMessage">Error message if failed (optional)</param>
    /// <param name="metadata">Additional metadata (optional, will be serialized as JSON)</param>
    /// <returns>Audit log entry</returns>
    Task<AuditLog> LogDataAccessAsync(
        string userId,
        string entityType,
        string entityId,
        string action,
        string status = "Success",
        string? errorMessage = null,
        string? metadata = null);

    /// <summary>
    /// Logs an admin action (user management, plan changes).
    /// </summary>
    /// <param name="adminUserId">Admin user ID (will be sanitized/hashed)</param>
    /// <param name="targetUserId">Target user ID (will be sanitized/hashed)</param>
    /// <param name="action">Admin action performed (create_user, update_user, change_plan, delete_user)</param>
    /// <param name="changeDetails">Details of the change (optional)</param>
    /// <param name="reason">Reason for the admin action (optional)</param>
    /// <returns>Audit log entry</returns>
    Task<AuditLog> LogAdminActionAsync(
        string adminUserId,
        string targetUserId,
        string action,
        string? changeDetails = null,
        string? reason = null);

    /// <summary>
    /// Logs a rate limit violation.
    /// </summary>
    /// <param name="userId">User ID (optional, will be sanitized/hashed if provided)</param>
    /// <param name="endpoint">API endpoint that was rate limited</param>
    /// <param name="ipAddress">IP address of the client (optional)</param>
    /// <param name="limit">Rate limit that was exceeded</param>
    /// <param name="windowSeconds">Rate limit window in seconds</param>
    /// <returns>Audit log entry</returns>
    Task<AuditLog> LogRateLimitViolationAsync(
        string? userId,
        string endpoint,
        string? ipAddress = null,
        int limit = 100,
        int windowSeconds = 60);

    /// <summary>
    /// Logs a security event (authorization failure, suspicious activity).
    /// </summary>
    /// <param name="userId">User ID (will be sanitized/hashed)</param>
    /// <param name="eventType">Type of security event (unauthorized_access, suspicious_activity, etc.)</param>
    /// <param name="description">Description of the security event</param>
    /// <param name="ipAddress">IP address involved (optional)</param>
    /// <param name="severity">Severity level (Low, Medium, High, Critical)</param>
    /// <returns>Audit log entry</returns>
    Task<AuditLog> LogSecurityEventAsync(
        string? userId,
        string eventType,
        string description,
        string? ipAddress = null,
        string severity = "Medium");

    /// <summary>
    /// Gets audit logs for a specific entity.
    /// </summary>
    /// <param name="entityType">Type of entity</param>
    /// <param name="entityId">ID of the entity</param>
    /// <returns>Array of audit logs</returns>
    Task<AuditLog[]> GetAuditLogsAsync(string entityType, string entityId);

    /// <summary>
    /// Gets recent audit logs.
    /// </summary>
    /// <param name="limit">Maximum number of entries to return</param>
    /// <returns>Array of recent audit logs</returns>
    Task<AuditLog[]> GetRecentAuditLogsAsync(int limit = 100);
}
