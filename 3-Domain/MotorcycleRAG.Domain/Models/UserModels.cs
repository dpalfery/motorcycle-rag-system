using System;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.Models
{
    /// <summary>
    /// User model representing application users
    /// </summary>
    public class User
    {
        /// <summary>
        /// Unique identifier for the user
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// User's email address
        /// </summary>
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// User's display name
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// User's first name
        /// </summary>
        public string FirstName { get; set; } = string.Empty;

        /// <summary>
        /// User's last name
        /// </summary>
        public string LastName { get; set; } = string.Empty;

        /// <summary>
        /// Whether the user account is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Date when the user was created
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Date when the user was last updated
        /// </summary>
        public DateTime? LastUpdatedDate { get; set; }

        /// <summary>
        /// User's subscription plan ID
        /// </summary>
        public string PlanId { get; set; } = string.Empty;

        /// <summary>
        /// Authentication provider (e.g., Azure AD, B2C)
        /// </summary>
        public string AuthProvider { get; set; } = string.Empty;

        /// <summary>
        /// Provider-specific user ID
        /// </summary>
        public string ProviderUserId { get; set; } = string.Empty;
    }

    /// <summary>
    /// User plan model
    /// </summary>
    public class UserPlan
    {
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