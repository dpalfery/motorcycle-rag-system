using System;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs {
    /// <summary>
    /// User plan model
    /// </summary>
    public class UserPlan {
        /// <summary>
        /// Unique identifier for the plan
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Plan name
        /// </summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Plan description
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Daily request limit for this plan
        /// </summary>
        public int DailyRequestLimit { get; set; } = 100;

        /// <summary>
        /// Whether this is a paid plan
        /// </summary>
        public bool IsPaid { get; set; } = false;

        /// <summary>
        /// Date when the plan was created
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}
