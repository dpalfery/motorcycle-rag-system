using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Application service for audit logging at the business logic layer.
/// Centralizes audit event logging for authentication, configuration changes, data access, and security events.
/// Implements OWASP ASVS Level 2 logging and monitoring requirements (ASVS 7.1.1-7.1.2).
///
/// Security Notes:
/// - User IDs are sanitized by hashing with SHA256 (one-way) to prevent direct PII exposure in logs
/// - Email addresses and sensitive data are logged but with redaction where appropriate
/// - All audit events include correlation IDs for request traceability
/// - Timestamps use UTC to prevent timezone confusion
/// - IP addresses are logged (optional) for forensic analysis
/// </summary>
public class AuditService : IAuditService
{
    private readonly IAuditRepository _auditRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICorrelationService _correlationService;
    private readonly ILogger<AuditService> _logger;

    /// <summary>
    /// Initializes a new instance of AuditService.
    /// </summary>
    public AuditService(
        IAuditRepository auditRepository,
        ICurrentUserService currentUserService,
        ICorrelationService correlationService,
        ILogger<AuditService> logger)
    {
        _auditRepository = auditRepository ?? throw new ArgumentNullException(nameof(auditRepository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _correlationService = correlationService ?? throw new ArgumentNullException(nameof(correlationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Logs a user authentication event (login).
    /// OWASP ASVS 2.1.1: All authentication attempts are logged
    /// </summary>
    public async Task<AuditLog> LogAuthenticationAsync(
        string userId,
        string? email = null,
        string? ipAddress = null,
        string? userAgent = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be empty", nameof(userId));

        var sanitizedUserId = SanitizeUserId(userId);
        var correlationId = _correlationService.GetOrGenerateCorrelationId();

        var auditLog = new AuditLog
        {
            UserId = sanitizedUserId,
            UserEmail = email, // Stored for audit trail but not exposed in logs directly
            Action = "authentication_success",
            EntityType = "User",
            EntityId = userId, // Store original ID for audit trail
            Status = "Success",
            ActionDate = DateTime.UtcNow,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Metadata = JsonSerializer.Serialize(new { CorrelationId = correlationId })
        };

        var createdLog = await _auditRepository.CreateAuditLogAsync(auditLog);

        // Structured logging with redacted user details
        _logger.LogInformation(
            "User authentication successful. UserId: {SanitizedUserId}, Email: {Email}, CorrelationId: {CorrelationId}",
            sanitizedUserId,
            RedactEmail(email),
            correlationId);

        return createdLog;
    }

    /// <summary>
    /// Logs a user logout event.
    /// OWASP ASVS 2.3.1: Logout is properly logged
    /// </summary>
    public async Task<AuditLog> LogLogoutAsync(
        string userId,
        string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be empty", nameof(userId));

        var sanitizedUserId = SanitizeUserId(userId);
        var correlationId = _correlationService.GetOrGenerateCorrelationId();

        var auditLog = new AuditLog
        {
            UserId = sanitizedUserId,
            Action = "logout",
            EntityType = "User",
            EntityId = userId,
            Status = "Success",
            ActionDate = DateTime.UtcNow,
            IpAddress = ipAddress,
            Metadata = JsonSerializer.Serialize(new { CorrelationId = correlationId })
        };

        var createdLog = await _auditRepository.CreateAuditLogAsync(auditLog);

        _logger.LogInformation(
            "User logout. UserId: {SanitizedUserId}, CorrelationId: {CorrelationId}",
            sanitizedUserId,
            correlationId);

        return createdLog;
    }

    /// <summary>
    /// Logs a failed authentication attempt.
    /// OWASP ASVS 2.1.2: Failed authentication attempts are logged
    /// </summary>
    public async Task<AuditLog> LogFailedAuthenticationAsync(
        string? email,
        string reason,
        string? ipAddress = null,
        string? userAgent = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be empty", nameof(reason));

        var correlationId = _correlationService.GetOrGenerateCorrelationId();
        var sanitizedEmail = RedactEmail(email); // Redact email in case of brute force attempts

        var auditLog = new AuditLog
        {
            UserEmail = email, // Store original for audit trail
            Action = "authentication_failure",
            EntityType = "User",
            EntityId = email ?? "unknown",
            Status = "Failure",
            ErrorMessage = reason,
            ActionDate = DateTime.UtcNow,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Metadata = JsonSerializer.Serialize(new { CorrelationId = correlationId, Reason = reason })
        };

        var createdLog = await _auditRepository.CreateAuditLogAsync(auditLog);

        // Log at warning level for failed authentication attempts
        _logger.LogWarning(
            "Authentication failure. Email: {Email}, Reason: {Reason}, IpAddress: {IpAddress}, CorrelationId: {CorrelationId}",
            sanitizedEmail,
            reason,
            ipAddress ?? "unknown",
            correlationId);

        return createdLog;
    }

    /// <summary>
    /// Logs a configuration change (MCP tools, web sources, etc.).
    /// OWASP ASVS 7.1.1: Sensitive and important system events are logged
    /// </summary>
    public async Task<AuditLog> LogConfigurationChangeAsync(
        string userId,
        string entityType,
        string entityId,
        string action,
        string? beforeValue = null,
        string? afterValue = null,
        string? changeReason = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be empty", nameof(userId));

        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("Entity type cannot be empty", nameof(entityType));

        if (string.IsNullOrWhiteSpace(entityId))
            throw new ArgumentException("Entity ID cannot be empty", nameof(entityId));

        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action cannot be empty", nameof(action));

        var sanitizedUserId = SanitizeUserId(userId);
        var correlationId = _correlationService.GetOrGenerateCorrelationId();

        var metadata = new
        {
            CorrelationId = correlationId,
            ChangeReason = changeReason
        };

        var auditLog = new AuditLog
        {
            UserId = sanitizedUserId,
            Action = $"config_change_{action}",
            EntityType = entityType,
            EntityId = entityId,
            OldValue = beforeValue,
            NewValue = afterValue,
            Status = "Success",
            ActionDate = DateTime.UtcNow,
            Metadata = JsonSerializer.Serialize(metadata)
        };

        var createdLog = await _auditRepository.CreateAuditLogAsync(auditLog);

        _logger.LogInformation(
            "Configuration changed. EntityType: {EntityType}, EntityId: {EntityId}, Action: {Action}, UserId: {SanitizedUserId}, Reason: {Reason}, CorrelationId: {CorrelationId}",
            entityType,
            entityId,
            action,
            sanitizedUserId,
            changeReason ?? "not specified",
            correlationId);

        return createdLog;
    }

    /// <summary>
    /// Logs a data access operation (ingestion, search, deletion).
    /// OWASP ASVS 7.1.3: Access to important data is logged
    /// </summary>
    public async Task<AuditLog> LogDataAccessAsync(
        string userId,
        string entityType,
        string entityId,
        string action,
        string status = "Success",
        string? errorMessage = null,
        string? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be empty", nameof(userId));

        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("Entity type cannot be empty", nameof(entityType));

        if (string.IsNullOrWhiteSpace(entityId))
            throw new ArgumentException("Entity ID cannot be empty", nameof(entityId));

        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action cannot be empty", nameof(action));

        var sanitizedUserId = SanitizeUserId(userId);
        var correlationId = _correlationService.GetOrGenerateCorrelationId();

        var fullMetadata = new
        {
            CorrelationId = correlationId,
            AdditionalData = metadata
        };

        var auditLog = new AuditLog
        {
            UserId = sanitizedUserId,
            Action = $"data_access_{action}",
            EntityType = entityType,
            EntityId = entityId,
            Status = status,
            ErrorMessage = errorMessage,
            ActionDate = DateTime.UtcNow,
            Metadata = JsonSerializer.Serialize(fullMetadata)
        };

        var createdLog = await _auditRepository.CreateAuditLogAsync(auditLog);

        var logLevel = status == "Success" ? LogLevel.Information : LogLevel.Warning;
        _logger.Log(
            logLevel,
            "Data access operation. EntityType: {EntityType}, EntityId: {EntityId}, Action: {Action}, Status: {Status}, UserId: {SanitizedUserId}, CorrelationId: {CorrelationId}",
            entityType,
            entityId,
            action,
            status,
            sanitizedUserId,
            correlationId);

        return createdLog;
    }

    /// <summary>
    /// Logs an admin action (user management, plan changes).
    /// OWASP ASVS 7.1.1: High-level privileged operations are logged
    /// </summary>
    public async Task<AuditLog> LogAdminActionAsync(
        string adminUserId,
        string targetUserId,
        string action,
        string? changeDetails = null,
        string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(adminUserId))
            throw new ArgumentException("Admin user ID cannot be empty", nameof(adminUserId));

        if (string.IsNullOrWhiteSpace(targetUserId))
            throw new ArgumentException("Target user ID cannot be empty", nameof(targetUserId));

        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action cannot be empty", nameof(action));

        var sanitizedAdminUserId = SanitizeUserId(adminUserId);
        var sanitizedTargetUserId = SanitizeUserId(targetUserId);
        var correlationId = _correlationService.GetOrGenerateCorrelationId();

        var metadata = new
        {
            CorrelationId = correlationId,
            ChangeDetails = changeDetails,
            Reason = reason
        };

        var auditLog = new AuditLog
        {
            UserId = sanitizedAdminUserId,
            Action = $"admin_{action}",
            EntityType = "User",
            EntityId = targetUserId,
            Status = "Success",
            ActionDate = DateTime.UtcNow,
            Metadata = JsonSerializer.Serialize(metadata)
        };

        var createdLog = await _auditRepository.CreateAuditLogAsync(auditLog);

        // Log admin actions at high priority
        _logger.LogInformation(
            "Admin action performed. Action: {Action}, AdminUserId: {AdminUserId}, TargetUserId: {TargetUserId}, Reason: {Reason}, CorrelationId: {CorrelationId}",
            action,
            sanitizedAdminUserId,
            sanitizedTargetUserId,
            reason ?? "not specified",
            correlationId);

        return createdLog;
    }

    /// <summary>
    /// Logs a rate limit violation.
    /// OWASP ASVS 6.2.1: Rate limiting violations are logged
    /// </summary>
    public async Task<AuditLog> LogRateLimitViolationAsync(
        string? userId,
        string endpoint,
        string? ipAddress = null,
        int limit = 100,
        int windowSeconds = 60)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint cannot be empty", nameof(endpoint));

        var sanitizedUserId = userId != null ? SanitizeUserId(userId) : "anonymous";
        var correlationId = _correlationService.GetOrGenerateCorrelationId();

        var metadata = new
        {
            CorrelationId = correlationId,
            Endpoint = endpoint,
            Limit = limit,
            WindowSeconds = windowSeconds
        };

        var auditLog = new AuditLog
        {
            UserId = sanitizedUserId,
            Action = "rate_limit_exceeded",
            EntityType = "RateLimiter",
            EntityId = endpoint,
            Status = "Failure",
            ActionDate = DateTime.UtcNow,
            IpAddress = ipAddress,
            Metadata = JsonSerializer.Serialize(metadata),
            ErrorMessage = $"Rate limit exceeded: {limit} requests per {windowSeconds} seconds"
        };

        var createdLog = await _auditRepository.CreateAuditLogAsync(auditLog);

        // Log rate limit violations at warning level
        _logger.LogWarning(
            "Rate limit violation. Endpoint: {Endpoint}, UserId: {SanitizedUserId}, IpAddress: {IpAddress}, Limit: {Limit}/{WindowSeconds}s, CorrelationId: {CorrelationId}",
            endpoint,
            sanitizedUserId,
            ipAddress ?? "unknown",
            limit,
            windowSeconds,
            correlationId);

        return createdLog;
    }

    /// <summary>
    /// Logs a security event (authorization failure, suspicious activity).
    /// OWASP ASVS 7.1.1: Security events are logged
    /// </summary>
    public async Task<AuditLog> LogSecurityEventAsync(
        string? userId,
        string eventType,
        string description,
        string? ipAddress = null,
        string severity = "Medium")
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type cannot be empty", nameof(eventType));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be empty", nameof(description));

        var sanitizedUserId = userId != null ? SanitizeUserId(userId) : "system";
        var correlationId = _correlationService.GetOrGenerateCorrelationId();

        var metadata = new
        {
            CorrelationId = correlationId,
            Severity = severity,
            EventType = eventType
        };

        var auditLog = new AuditLog
        {
            UserId = sanitizedUserId,
            Action = $"security_event_{eventType}",
            EntityType = "Security",
            EntityId = eventType,
            Status = "Failure",
            ErrorMessage = description,
            ActionDate = DateTime.UtcNow,
            IpAddress = ipAddress,
            Metadata = JsonSerializer.Serialize(metadata)
        };

        var createdLog = await _auditRepository.CreateAuditLogAsync(auditLog);

        // Map severity to log level
        var logLevel = severity switch
        {
            "Critical" => LogLevel.Critical,
            "High" => LogLevel.Error,
            "Medium" => LogLevel.Warning,
            _ => LogLevel.Information
        };

        _logger.Log(
            logLevel,
            "Security event logged. EventType: {EventType}, Description: {Description}, Severity: {Severity}, UserId: {SanitizedUserId}, IpAddress: {IpAddress}, CorrelationId: {CorrelationId}",
            eventType,
            description,
            severity,
            sanitizedUserId,
            ipAddress ?? "unknown",
            correlationId);

        return createdLog;
    }

    /// <summary>
    /// Gets audit logs for a specific entity.
    /// </summary>
    public async Task<AuditLog[]> GetAuditLogsAsync(string entityType, string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("Entity type cannot be empty", nameof(entityType));

        if (string.IsNullOrWhiteSpace(entityId))
            throw new ArgumentException("Entity ID cannot be empty", nameof(entityId));

        try
        {
            var logs = await _auditRepository.GetAuditLogsByEntityAsync(entityType, entityId);
            _logger.LogDebug(
                "Retrieved {Count} audit logs for entity {EntityType}:{EntityId}",
                logs.Length,
                entityType,
                entityId);

            return logs;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error retrieving audit logs for entity {EntityType}:{EntityId}",
                entityType,
                entityId);
            throw;
        }
    }

    /// <summary>
    /// Gets recent audit logs.
    /// </summary>
    public async Task<AuditLog[]> GetRecentAuditLogsAsync(int limit = 100)
    {
        if (limit <= 0 || limit > 10000)
            throw new ArgumentException("Limit must be between 1 and 10000", nameof(limit));

        try
        {
            var logs = await _auditRepository.GetRecentAuditLogsAsync(limit);
            _logger.LogDebug("Retrieved {Count} recent audit logs", logs.Length);
            return logs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving recent audit logs");
            throw;
        }
    }

    /// <summary>
    /// Sanitizes a user ID by hashing it with SHA256 (one-way hash).
    /// This prevents direct user ID exposure in logs while maintaining uniqueness for tracking.
    /// OWASP ASVS 7.1: PII protection in logs
    /// </summary>
    private static string SanitizeUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return "unknown";

        try
        {
            // Use SHA256 for consistent, one-way hashing
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(userId));
                // Return first 16 characters of hex representation for readability
                return Convert.ToHexString(hashedBytes)[..16];
            }
        }
        catch
        {
            // Fallback if hashing fails
            return "redacted";
        }
    }

    /// <summary>
    /// Redacts an email address for logging purposes.
    /// Shows first character and domain, hides middle.
    /// Example: user@example.com → u*****@example.com
    /// </summary>
    private static string RedactEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "[not provided]";

        var parts = email.Split('@');
        if (parts.Length != 2)
            return "[invalid]";

        var localPart = parts[0];
        var domain = parts[1];

        if (localPart.Length <= 2)
            return $"*@{domain}";

        var asterisks = new string('*', localPart.Length - 1);
        var redacted = $"{localPart[0]}{asterisks}@{domain}";
        return redacted;
    }
}
