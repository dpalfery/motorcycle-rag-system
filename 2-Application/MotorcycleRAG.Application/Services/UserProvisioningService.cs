using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;


namespace MotorcycleRAG.Application.Services {
    /// <summary>
    /// Service for provisioning and updating users on login
    /// </summary>
    public class UserProvisioningService : IUserProvisioningService {
        private readonly IUserRepository _userRepository;
        private readonly IUserIdentityRepository _userIdentityRepository;
        private readonly IPlanRepository _planRepository;
        private readonly ILogger<UserProvisioningService> _logger;

        /// <summary>
        /// Initializes a new instance of UserProvisioningService
        /// </summary>
        /// <param name="userRepository">User repository</param>
        /// <param name="planRepository">Plan repository</param>
        /// <param name="logger">Logger</param>
        public UserProvisioningService(
            IUserRepository userRepository,
            IUserIdentityRepository userIdentityRepository,
            IPlanRepository planRepository,
            ILogger<UserProvisioningService> logger) {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _userIdentityRepository = userIdentityRepository ?? throw new ArgumentNullException(nameof(userIdentityRepository));
            _planRepository = planRepository ?? throw new ArgumentNullException(nameof(planRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

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
        public async Task<UserDTO> ProvisionOrUpdateUserAsync(
            string userId,
            string email,
            string? displayName,
            string? firstName,
            string? lastName,
            string authProvider,
            string? providerUserId) {
            if (string.IsNullOrWhiteSpace(userId)) {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            if (string.IsNullOrWhiteSpace(email)) {
                throw new ArgumentException("Email cannot be null or empty", nameof(email));
            }

            if (string.IsNullOrWhiteSpace(authProvider)) {
                throw new ArgumentException("Auth provider cannot be null or empty", nameof(authProvider));
            }

            var provider = ResolveIdentityProvider(authProvider);
            var reconciledUser = await ReconcileApprovedUserAsync(
                issuer: string.Empty,
                subject: userId,
                email: email,
                displayName: displayName,
                firstName: firstName,
                lastName: lastName,
                provider: provider,
                providerUserId: providerUserId,
                objectId: null);

            if (reconciledUser == null) {
                _logger.LogWarning("Rejected sign-in for unapproved user {Email}", email);
                throw new InvalidOperationException("User must be approved before sign-in is allowed.");
            }

            return reconciledUser;
        }

        /// <summary>
        /// Resolves the approved internal managed-user ID for a provider-authenticated sign-in.
        /// </summary>
        public async Task<string?> ResolveManagedUserIdAsync(
            string issuer,
            string subject,
            string email,
            IdentityProvider provider) {
            if (string.IsNullOrWhiteSpace(email)) {
                throw new ArgumentException("Email cannot be null or empty", nameof(email));
            }

            return await _userIdentityRepository.GetManagedUserIdAsync(issuer, subject, email, provider);
        }

        /// <summary>
        /// Gets the approved managed user for a provider-authenticated sign-in.
        /// </summary>
        public async Task<UserDTO?> GetApprovedManagedUserAsync(
            string issuer,
            string subject,
            string email,
            IdentityProvider provider) {
            var managedUserId = await ResolveManagedUserIdAsync(issuer, subject, email, provider);
            if (string.IsNullOrWhiteSpace(managedUserId)) {
                _logger.LogInformation(
                    "No approved managed user found for provider-authenticated sign-in {Provider}/{Email}",
                    provider,
                    email);
                return null;
            }

            var user = await _userRepository.GetUserByIdAsync(managedUserId);
            return IsUserAllowedToSignIn(user) ? user : null;
        }

        /// <summary>
        /// Reconciles an approved or legacy active user with provider identity claims without creating a new user.
        /// </summary>
        public async Task<UserDTO?> ReconcileApprovedUserAsync(
            string issuer,
            string subject,
            string email,
            string? displayName,
            string? firstName,
            string? lastName,
            IdentityProvider provider,
            string? providerUserId,
            string? objectId) {
            if (string.IsNullOrWhiteSpace(email)) {
                throw new ArgumentException("Email cannot be null or empty", nameof(email));
            }

            var approvedUser = await GetApprovedManagedUserAsync(issuer, subject, email, provider)
                ?? await GetSameProviderEmailFallbackUserAsync(email, provider);

            if (!IsUserAllowedToSignIn(approvedUser)) {
                return null;
            }

            var user = await UpdateUserAsync(
                approvedUser!,
                email,
                displayName,
                firstName,
                lastName,
                provider.ToString(),
                providerUserId);

            var identityLinkSaved = await _userIdentityRepository.UpsertAsync(
                user.Id,
                provider,
                email,
                string.IsNullOrWhiteSpace(issuer) ? null : issuer,
                string.IsNullOrWhiteSpace(subject) ? null : subject,
                providerUserId,
                objectId);

            if (!identityLinkSaved) {
                _logger.LogWarning("Identity link was not updated for approved user {UserId}", user.Id);
            }

            if (user.AccessState == ManagedUserAccessState.None && user.IsEnabled) {
                user.AccessState = ManagedUserAccessState.Active;
                user.LastUpdatedDate = DateTime.UtcNow;
                await _userRepository.UpdateUserAsync(user);
            }

            return user;
        }

        /// <summary>
        /// Updates an existing user with latest information from authentication provider
        /// </summary>
        private async Task<UserDTO> UpdateUserAsync(
            UserDTO existingUser,
            string email,
            string? displayName,
            string? firstName,
            string? lastName,
            string authProvider,
            string? providerUserId) {
            var needsUpdate = false;

            // Update email if changed
            if (existingUser.Email != email) {
                existingUser.Email = email;
                needsUpdate = true;
            }

            // Update display name if provided and changed
            if (!string.IsNullOrWhiteSpace(displayName) && existingUser.DisplayName != displayName) {
                existingUser.DisplayName = displayName;
                needsUpdate = true;
            }

            // Update first name if provided and changed
            if (!string.IsNullOrWhiteSpace(firstName) && existingUser.FirstName != firstName) {
                existingUser.FirstName = firstName;
                needsUpdate = true;
            }

            // Update last name if provided and changed
            if (!string.IsNullOrWhiteSpace(lastName) && existingUser.LastName != lastName) {
                existingUser.LastName = lastName;
                needsUpdate = true;
            }

            // Update provider user ID if provided and changed
            if (!string.IsNullOrWhiteSpace(providerUserId) && existingUser.ProviderUserId != providerUserId) {
                existingUser.ProviderUserId = providerUserId;
                needsUpdate = true;
            }

            // Update auth provider if changed
            if (existingUser.AuthProvider != authProvider) {
                existingUser.AuthProvider = authProvider;
                needsUpdate = true;
            }

            if (needsUpdate) {
                existingUser.LastUpdatedDate = DateTime.UtcNow;
                await _userRepository.UpdateUserAsync(existingUser);
                _logger.LogInformation("Updated user {UserId} on login", existingUser.Id);
            }
            else {
                _logger.LogDebug("User {UserId} no updates needed on login", existingUser.Id);
            }

            return existingUser;
        }

        private async Task<UserDTO?> GetSameProviderEmailFallbackUserAsync(string email, IdentityProvider provider) {
            var existingUser = await _userRepository.GetUserByEmailAsync(email);
            if (existingUser == null) {
                return null;
            }

            if (!IsUserAllowedToSignIn(existingUser)) {
                return null;
            }

            if (!MatchesProvider(existingUser.AuthProvider, provider)) {
                _logger.LogWarning(
                    "Blocked sign-in for {Email} because the authenticated provider {Provider} does not match the approved provider {ApprovedProvider}",
                    email,
                    provider,
                    existingUser.AuthProvider);
                return null;
            }

            return existingUser;
        }

        private static bool IsUserAllowedToSignIn(UserDTO? user) {
            return user != null
                && user.IsEnabled
                && user.AccessState != ManagedUserAccessState.Cancelled
                && user.AccessState != ManagedUserAccessState.Disabled;
        }

        private static bool MatchesProvider(string? authProvider, IdentityProvider provider) {
            if (string.IsNullOrWhiteSpace(authProvider)) {
                return false;
            }

            var isGoogleProvider = authProvider.Contains("google", StringComparison.OrdinalIgnoreCase);
            return provider == IdentityProvider.Google ? isGoogleProvider : !isGoogleProvider;
        }

        private static IdentityProvider ResolveIdentityProvider(string authProvider) {
            if (!string.IsNullOrWhiteSpace(authProvider) && authProvider.Contains("google", StringComparison.OrdinalIgnoreCase)) {
                return IdentityProvider.Google;
            }

            return IdentityProvider.Microsoft;
        }
    }
}
