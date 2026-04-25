using System;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs {
    /// <summary>
    /// User model representing application users
    /// </summary>
    public class UserDTO {
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
        /// User's assigned onboarding tier label.
        /// </summary>
        public TierLabel? TierLabel { get; set; }

        /// <summary>
        /// User's effective access state.
        /// </summary>
        public ManagedUserAccessState AccessState { get; set; } = ManagedUserAccessState.None;

        /// <summary>
        /// When access was cancelled, if applicable.
        /// </summary>
        public DateTime? CancelledAtUtc { get; set; }

        /// <summary>
        /// The internal user ID that cancelled access.
        /// </summary>
        public string? CancelledByUserId { get; set; }

        /// <summary>
        /// The reason access was cancelled.
        /// </summary>
        public string? CancelReason { get; set; }

        /// <summary>
        /// Authentication provider (e.g., Azure AD, B2C)
        /// </summary>
        public string AuthProvider { get; set; } = string.Empty;

        /// <summary>
        /// Provider-specific user ID
        /// </summary>
        public string ProviderUserId { get; set; } = string.Empty;

        /// <summary>
        /// SQL rowversion rendered as a hex string for optimistic concurrency.
        /// </summary>
        public string RowVersion { get; set; } = string.Empty;
    }
}
