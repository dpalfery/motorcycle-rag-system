using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models.DTOs;


namespace MotorcycleRAG.Contracts.Interfaces {
    /// <summary>
    /// Service interface for Entra external-identity provisioning and access reconciliation.
    /// </summary>
    public interface IExternalIdentityProvisioningService {
        /// <summary>
        /// Provisions or invites an approved external identity and applies its mapped access.
        /// </summary>
        Task<string> ProvisionApprovedUserAsync(string email, string displayName, TierLabel tier, IdentityProvider provider);

        /// <summary>
        /// Reconciles tier-based downstream access for an existing external identity.
        /// </summary>
        Task ReconcileTierAssignmentsAsync(string externalDirectoryObjectId, TierLabel tier);

        /// <summary>
        /// Revokes downstream access for an existing external identity.
        /// </summary>
        Task RevokeAccessAsync(string externalDirectoryObjectId);
    }
}