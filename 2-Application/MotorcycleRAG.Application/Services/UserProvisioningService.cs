using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;


namespace MotorcycleRAG.Application.Services
{
    /// <summary>
    /// Service for provisioning and updating users on login
    /// </summary>
    public class UserProvisioningService : IUserProvisioningService
    {
        private readonly IUserRepository _userRepository;
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
            IPlanRepository planRepository,
            ILogger<UserProvisioningService> logger)
        {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
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
            string? providerUserId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email cannot be null or empty", nameof(email));
            }

            if (string.IsNullOrWhiteSpace(authProvider))
            {
                throw new ArgumentException("Auth provider cannot be null or empty", nameof(authProvider));
            }

            // Try to find existing user by ID or email
            var existingUser = await _userRepository.GetUserByIdAsync(userId) 
                            ?? await _userRepository.GetUserByEmailAsync(email);

            if (existingUser != null)
            {
                // Update existing user
                _logger.LogInformation("Updating existing user {UserId} on login", existingUser.Id);
                return await UpdateUserAsync(existingUser, email, displayName, firstName, lastName, authProvider, providerUserId);
            }
            else
            {
                // Create new user
                _logger.LogInformation("Creating new user {UserId} on login", userId);
                return await CreateUserAsync(userId, email, displayName, firstName, lastName, authProvider, providerUserId);
            }
        }

        /// <summary>
        /// Creates a new user with default plan assignment
        /// </summary>
        private async Task<UserDTO> CreateUserAsync(
            string userId,
            string email,
            string? displayName,
            string? firstName,
            string? lastName,
            string authProvider,
            string? providerUserId)
        {
            // Get default plan (typically a free tier)
            var defaultPlan = await _planRepository.GetPlanByNameAsync("Free") 
                           ?? await _planRepository.GetPlanByNameAsync("Basic");

            var newUser = new UserDTO
            {
                Id = userId,
                Email = email,
                DisplayName = displayName ?? email.Split('@')[0],
                FirstName = firstName ?? string.Empty,
                LastName = lastName ?? string.Empty,
                IsEnabled = true,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                PlanId = defaultPlan?.Id ?? string.Empty,
                AuthProvider = authProvider,
                ProviderUserId = providerUserId ?? userId
            };

            var createdUser = await _userRepository.CreateUserAsync(newUser);
            _logger.LogInformation("Created new user {UserId} with plan {PlanId}", createdUser.Id, createdUser.PlanId);
            return createdUser;
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
            string? providerUserId)
        {
            var needsUpdate = false;

            // Update email if changed
            if (existingUser.Email != email)
            {
                existingUser.Email = email;
                needsUpdate = true;
            }

            // Update display name if provided and changed
            if (!string.IsNullOrWhiteSpace(displayName) && existingUser.DisplayName != displayName)
            {
                existingUser.DisplayName = displayName;
                needsUpdate = true;
            }

            // Update first name if provided and changed
            if (!string.IsNullOrWhiteSpace(firstName) && existingUser.FirstName != firstName)
            {
                existingUser.FirstName = firstName;
                needsUpdate = true;
            }

            // Update last name if provided and changed
            if (!string.IsNullOrWhiteSpace(lastName) && existingUser.LastName != lastName)
            {
                existingUser.LastName = lastName;
                needsUpdate = true;
            }

            // Update provider user ID if provided and changed
            if (!string.IsNullOrWhiteSpace(providerUserId) && existingUser.ProviderUserId != providerUserId)
            {
                existingUser.ProviderUserId = providerUserId;
                needsUpdate = true;
            }

            // Update auth provider if changed
            if (existingUser.AuthProvider != authProvider)
            {
                existingUser.AuthProvider = authProvider;
                needsUpdate = true;
            }

            if (needsUpdate)
            {
                existingUser.LastUpdatedDate = DateTime.UtcNow;
                await _userRepository.UpdateUserAsync(existingUser);
                _logger.LogInformation("Updated user {UserId} on login", existingUser.Id);
            }
            else
            {
                _logger.LogDebug("User {UserId} no updates needed on login", existingUser.Id);
            }

            return existingUser;
        }
    }
}
