using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models.DTOs;


namespace MotorcycleRAG.Contracts.Interfaces {
    /// <summary>
    /// Service interface for user provisioning and update-on-login operations
    /// </summary>
    public interface IUserProvisioningService {
        /// <summary>
        /// Provisions or updates a user on login
        /// </summary>
        /// <param name="userId">User ID from authentication provider</param>
        /// <param name="email">User email</param>
        /// <param name="displayName">Display name</param>
        /// <param name="firstName">First name</param>
        /// <param name="lastName">Last name</param>
        /// <param name="authProvider">Authentication provider</param>
        /// <param name="providerUserId">Provider-specific user ID</param>
        /// <returns>Provisioned or updated user</returns>
        Task<UserDTO> ProvisionOrUpdateUserAsync(
            string userId,
            string email,
            string? displayName,
            string? firstName,
            string? lastName,
            string authProvider,
            string? providerUserId);

        /// <summary>
        /// Resolves the approved internal managed-user ID for a provider-authenticated sign-in.
        /// </summary>
        Task<string?> ResolveManagedUserIdAsync(
            string issuer,
            string subject,
            string email,
            IdentityProvider provider);

        /// <summary>
        /// Gets the approved managed user for a provider-authenticated sign-in.
        /// </summary>
        Task<UserDTO?> GetApprovedManagedUserAsync(
            string issuer,
            string subject,
            string email,
            IdentityProvider provider);

        /// <summary>
        /// Reconciles an approved or legacy active user with provider identity claims without creating a new user.
        /// </summary>
        Task<UserDTO?> ReconcileApprovedUserAsync(
            string issuer,
            string subject,
            string email,
            string? displayName,
            string? firstName,
            string? lastName,
            IdentityProvider provider,
            string? providerUserId,
            string? objectId);
    }
}
