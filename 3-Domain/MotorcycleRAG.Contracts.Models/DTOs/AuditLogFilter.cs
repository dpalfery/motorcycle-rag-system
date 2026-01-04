using System;

namespace MotorcycleRAG.Domain.DTOs
{
    /// <summary>
    /// Audit log query filter model
    /// </summary>
    public class AuditLogFilter
    {
        /// <summary>
        /// User ID to filter by
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// Entity type to filter by
        /// </summary>
        public string? EntityType { get; set; }

        /// <summary>
        /// Entity ID to filter by
        /// </summary>
        public string? EntityId { get; set; }

        /// <summary>
        /// Action type to filter by
        /// </summary>
        public string? Action { get; set; }

        /// <summary>
        /// Start date for filtering
        /// </summary>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// End date for filtering
        /// </summary>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// Maximum number of results to return
        /// </summary>
        public int Limit { get; set; } = 100;
    }
}
