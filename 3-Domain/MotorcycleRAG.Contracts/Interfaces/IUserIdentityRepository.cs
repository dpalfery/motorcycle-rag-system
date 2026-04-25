using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models.DTOs;


namespace MotorcycleRAG.Contracts.Interfaces {
    /// <summary>
    /// Repository interface for managed-user identity link persistence.
    /// </summary>
    public interface IUserIdentityRepository {
        /// <summary>
        /// Resolves the internal managed user ID for a provider-linked identity.
        /// </summary>
        Task<string?> GetManagedUserIdAsync(string issuer, string subject, string email, IdentityProvider provider);

        /// <summary>
        /// Gets the active provider identity link for a managed user.
        /// </summary>
        Task<UserIdentityLinkRecord?> GetActiveByManagedUserIdAsync(string managedUserId);

        /// <summary>
        /// Checks whether an active identity link already exists for a managed user.
        /// </summary>
        Task<bool> ExistsAsync(string managedUserId, IdentityProvider provider, string providerEmail);

        /// <summary>
        /// Creates or updates an identity link for a managed user.
        /// </summary>
        Task<bool> UpsertAsync(
            string managedUserId,
            IdentityProvider provider,
            string providerEmail,
            string? issuer,
            string? subject,
            string? providerUserId,
            string? externalDirectoryObjectId);

        /// <summary>
        /// Marks all active identity links for a managed user as revoked.
        /// </summary>
        Task<bool> MarkAccessRevokedAsync(string managedUserId);
    }
}