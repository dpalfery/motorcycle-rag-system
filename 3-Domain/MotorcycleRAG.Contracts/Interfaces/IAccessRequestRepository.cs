using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models.DTOs;


namespace MotorcycleRAG.Contracts.Interfaces {
    /// <summary>
    /// Repository interface for onboarding access-request persistence operations.
    /// </summary>
    public interface IAccessRequestRepository {
        /// <summary>
        /// Gets the public request status for a provider and email combination.
        /// </summary>
        Task<PublicAccessRequestResponse?> GetByProviderAndEmailAsync(string email, IdentityProvider provider);

        /// <summary>
        /// Gets the public request status by request identifier.
        /// </summary>
        Task<PublicAccessRequestResponse?> GetByRequestIdAsync(string requestId);

        /// <summary>
        /// Gets the admin orchestration record for a specific access request.
        /// </summary>
        Task<AccessRequestAdminRecord?> GetAdminRecordByRequestIdAsync(string requestId);

        /// <summary>
        /// Transitions a pending access request into approved onboarding-in-progress state.
        /// </summary>
        Task<AccessRequestAdminRecord?> BeginApprovalOnboardingAsync(string requestId, TierLabel tier, string expectedRowVersion, string? approvedByUserId);

        /// <summary>
        /// Retries onboarding for an already-approved request that previously failed.
        /// </summary>
        Task<AccessRequestAdminRecord?> RetryOnboardingAsync(string requestId, string expectedRowVersion);

        /// <summary>
        /// Marks approval-time onboarding as completed and persists linkage state.
        /// </summary>
        Task<AccessRequestAdminRecord?> CompleteOnboardingAsync(string requestId, string managedUserId, string externalDirectoryObjectId);

        /// <summary>
        /// Marks approval-time onboarding as failed while preserving partial linkage state for retry.
        /// </summary>
        Task<AccessRequestAdminRecord?> FailOnboardingAsync(string requestId, string failureCode, string failureMessage, string? managedUserId, string? externalDirectoryObjectId);

        /// <summary>
        /// Cancels a pending or failed access request.
        /// </summary>
        Task<AccessRequestAdminRecord?> CancelAsync(string requestId, string expectedRowVersion, string reason, string? cancelledByUserId);

        /// <summary>
        /// Creates a new pending access request.
        /// </summary>
        Task<PublicAccessRequestResponse> CreateAsync(CreateAccessRequestRequest request, string correlationId);

        /// <summary>
        /// Checks whether a pending request already exists for the provider and email combination.
        /// </summary>
        Task<bool> ExistsPendingAsync(string email, IdentityProvider provider);
    }
}