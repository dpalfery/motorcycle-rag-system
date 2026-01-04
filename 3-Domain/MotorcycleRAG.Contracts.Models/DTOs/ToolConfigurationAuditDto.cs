namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Represents a single audit entry for tool configuration changes.
/// Data contract for transferring audit information across layers.
/// </summary>
public class ToolConfigurationAuditEntry {
    /// <summary>
    /// Unique identifier for the audit entry.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// ID of the tool configuration being audited.
    /// </summary>
    public Guid ToolConfigurationId { get; set; }

    /// <summary>
    /// Tool ID for quick reference.
    /// </summary>
    public string ToolId { get; set; } = string.Empty;

    /// <summary>
    /// Action performed: "created", "updated", "enabled", "disabled", "deleted"
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// JSON snapshot of configuration before change (null for create).
    /// </summary>
    public string? BeforeJson { get; set; }

    /// <summary>
    /// JSON snapshot of configuration after change.
    /// </summary>
    public string? AfterJson { get; set; }

    /// <summary>
    /// User ID who made the change (null for system changes).
    /// WARNING: Sanitized for logging - redacted identifiable information.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Reason for the change (optional, for compliance).
    /// </summary>
    public string? ChangeReason { get; set; }

    /// <summary>
    /// Timestamp when change was recorded (UTC).
    /// </summary>
    public DateTime ChangedAt { get; set; }

    /// <summary>
    /// IP address of request origin (optional, for security audit).
    /// </summary>
    public string? IpAddress { get; set; }
}

/// <summary>
/// Summary statistics for audit trail.
/// Data contract for audit reporting.
/// </summary>
public class ToolConfigurationAuditSummary {
    /// <summary>
    /// Total number of audit entries recorded.
    /// </summary>
    public int TotalEntries { get; set; }

    /// <summary>
    /// Number of unique tool configurations audited.
    /// </summary>
    public int UniqueTools { get; set; }

    /// <summary>
    /// Number of unique users who made changes.
    /// </summary>
    public int UniqueUsers { get; set; }

    /// <summary>
    /// Oldest audit entry timestamp.
    /// </summary>
    public DateTime? OldestEntry { get; set; }

    /// <summary>
    /// Most recent audit entry timestamp.
    /// </summary>
    public DateTime? LatestEntry { get; set; }
}
