using System;

namespace MotorcycleRAG.Contracts.Models.DTOs {
    /// <summary>
    /// Daily usage summary model
    /// </summary>
    public class DailyUsageSummary {
        /// <summary>
        /// User ID
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Date of usage
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Total count of requests
        /// </summary>
        public int RequestCount { get; set; }

        /// <summary>
        /// Average request duration in milliseconds
        /// </summary>
        public double AverageDurationMs { get; set; }

        /// <summary>
        /// Success rate percentage
        /// </summary>
        public double SuccessRate { get; set; }
    }
}
