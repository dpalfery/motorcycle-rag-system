using System;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.DTOs
{
    /// <summary>
    /// Audit log model for tracking system changes
    /// </summary>
    public class AuditLog
    {
        /// <summary>
        /// Unique identifier for the audit log
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// User ID who performed the action
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// User email who performed the action
        /// </summary>
        public string? UserEmail { get; set; }

        /// <summary>
        /// Action that was performed
        /// </summary>
        [Required]
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Type of entity affected
        /// </summary>
        [Required]
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// ID of the entity affected
        /// </summary>
        [Required]
        public string EntityId { get; set; } = string.Empty;

        /// <summary>
        /// Old value (for updates)
        /// </summary>
        public string? OldValue { get; set; }

        /// <summary>
        /// New value (for updates)
        /// </summary>
        public string? NewValue { get; set; }

        /// <summary>
        /// Date and time when the action occurred
        /// </summary>
        public DateTime ActionDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// IP address of the user who performed the action
        /// </summary>
        public string? IpAddress { get; set; }

        /// <summary>
        /// User agent string
        /// </summary>
        public string? UserAgent { get; set; }

        /// <summary>
        /// Additional metadata about the action
        /// </summary>
        public string? Metadata { get; set; }

        /// <summary>
        /// Status of the action (Success/Failure)
        /// </summary>
        public string Status { get; set; } = "Success";

        /// <summary>
        /// Error message if action failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}
