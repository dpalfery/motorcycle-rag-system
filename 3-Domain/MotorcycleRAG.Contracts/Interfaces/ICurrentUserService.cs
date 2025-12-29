using System.Security.Claims;
using MotorcycleRAG.Contracts.Models;


namespace MotorcycleRAG.Contracts.Interfaces
{
    /// <summary>
    /// Service interface for resolving the current authenticated user from HTTP context claims
    /// </summary>
    public interface ICurrentUserService
    {
        /// <summary>
        /// Gets the current user's ID from claims
        /// </summary>
        /// <returns>User ID if authenticated, null otherwise</returns>
        string? UserId { get; }

        /// <summary>
        /// Gets the current user's email from claims
        /// </summary>
        /// <returns>User email if available, null otherwise</returns>
        string? Email { get; }

        /// <summary>
        /// Gets the current user's display name from claims
        /// </summary>
        /// <returns>Display name if available, null otherwise</returns>
        string? DisplayName { get; }

        /// <summary>
        /// Gets the current user's first name from claims
        /// </summary>
        /// <returns>First name if available, null otherwise</returns>
        string? FirstName { get; }

        /// <summary>
        /// Gets the current user's last name from claims
        /// </summary>
        /// <returns>Last name if available, null otherwise</returns>
        string? LastName { get; }

        /// <summary>
        /// Gets the authentication provider from claims
        /// </summary>
        /// <returns>Provider name if available, null otherwise</returns>
        string? AuthProvider { get; }

        /// <summary>
        /// Gets the provider-specific user ID from claims
        /// </summary>
        /// <returns>Provider user ID if available, null otherwise</returns>
        string? ProviderUserId { get; }

        /// <summary>
        /// Checks if the current user has a specific role
        /// </summary>
        /// <param name="role">Role to check</param>
        /// <returns>True if user has the role, false otherwise</returns>
        bool IsInRole(string role);

        /// <summary>
        /// Gets all claims for the current user
        /// </summary>
        /// <returns>Collection of claims</returns>
        IEnumerable<Claim> GetClaims();

        /// <summary>
        /// Checks if the current user is authenticated
        /// </summary>
        /// <returns>True if authenticated, false otherwise</returns>
        bool IsAuthenticated { get; }
    }
}
