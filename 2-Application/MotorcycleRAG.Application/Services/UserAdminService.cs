using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Utilities;


namespace MotorcycleRAG.Application.Services {
    /// <summary>
    /// Service for administrative user management operations
    /// </summary>
    public class UserAdminService : IUserAdminService {
        private readonly IUserRepository _userRepository;
        private readonly IPlanRepository _planRepository;
        private readonly ILogger<UserAdminService> _logger;

        /// <summary>
        /// Initializes a new instance of UserAdminService
        /// </summary>
        /// <param name="userRepository">User repository</param>
        /// <param name="planRepository">Plan repository</param>
        /// <param name="logger">Logger</param>
        public UserAdminService(
            IUserRepository userRepository,
            IPlanRepository planRepository,
            ILogger<UserAdminService> logger) {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _planRepository = planRepository ?? throw new ArgumentNullException(nameof(planRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Enables or disables a user account
        /// </summary>
        public async Task<UserDTO> SetUserEnabledStatusAsync(string userId, bool isEnabled) {
            if (string.IsNullOrWhiteSpace(userId)) {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            var user = await _userRepository.GetUserByIdAsync(userId);
            if (user == null) {
                _logger.LogWarning("User {UserId} not found when setting enabled status", LogSanitizer.Sanitize(userId));  // codeql[cs/log-forging]
                throw new ArgumentException($"User with ID {userId} not found", nameof(userId));
            }

            // Check if status is actually changing
            if (user.IsEnabled == isEnabled) {
                _logger.LogDebug("User {UserId} already has enabled status {IsEnabled}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(isEnabled));  // codeql[cs/log-forging]
                return user;
            }

            var success = await _userRepository.SetUserEnabledStatusAsync(userId, isEnabled);
            if (!success) {
                _logger.LogError("Failed to set enabled status for user {UserId}", LogSanitizer.Sanitize(userId));  // codeql[cs/log-forging]
                throw new InvalidOperationException($"Failed to update user {userId}");
            }

            user.IsEnabled = isEnabled;
            user.LastUpdatedDate = DateTime.UtcNow;

            _logger.LogInformation("User {UserId} enabled status set to {IsEnabled}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(isEnabled));  // codeql[cs/log-forging]
            return user;
        }

        /// <summary>
        /// Assigns a plan to a user
        /// </summary>
        public async Task<UserDTO> AssignPlanToUserAsync(string userId, string planId) {
            if (string.IsNullOrWhiteSpace(userId)) {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            if (string.IsNullOrWhiteSpace(planId)) {
                throw new ArgumentException("Plan ID cannot be null or empty", nameof(planId));
            }

            var user = await _userRepository.GetUserByIdAsync(userId);
            if (user == null) {
                _logger.LogWarning("User {UserId} not found when assigning plan", LogSanitizer.Sanitize(userId));  // codeql[cs/log-forging]
                throw new ArgumentException($"User with ID {userId} not found", nameof(userId));
            }

            var plan = await _planRepository.GetPlanByIdAsync(planId);
            if (plan == null) {
                _logger.LogWarning("Plan {PlanId} not found when assigning to user", LogSanitizer.Sanitize(planId));  // codeql[cs/log-forging]
                throw new ArgumentException($"Plan with ID {planId} not found", nameof(planId));
            }

            // Check if plan is already assigned
            if (user.PlanId == planId) {
                _logger.LogDebug("User {UserId} already has plan {PlanId}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(planId));  // codeql[cs/log-forging]
                return user;
            }

            user.PlanId = planId;
            user.LastUpdatedDate = DateTime.UtcNow;

            var success = await _userRepository.UpdateUserAsync(user);
            if (!success) {
                _logger.LogError("Failed to assign plan {PlanId} to user {UserId}", LogSanitizer.Sanitize(planId), LogSanitizer.Sanitize(userId));  // codeql[cs/log-forging]
                throw new InvalidOperationException($"Failed to assign plan {planId} to user {userId}");
            }

            _logger.LogInformation("Assigned plan {PlanId} to user {UserId}", LogSanitizer.Sanitize(planId), LogSanitizer.Sanitize(userId));  // codeql[cs/log-forging]
            return user;
        }

        /// <summary>
        /// Gets all users (admin view)
        /// </summary>
        public async Task<UserDTO[]> GetAllUsersAsync(int page = 1, int pageSize = 50) {
            if (page < 1) {
                throw new ArgumentException("Page number must be at least 1", nameof(page));
            }

            if (pageSize < 1 || pageSize > 100) {
                throw new ArgumentException("Page size must be between 1 and 100", nameof(pageSize));
            }

            _logger.LogInformation("Retrieving all users (page: {Page}, pageSize: {PageSize})", page, pageSize);
            return await _userRepository.GetUsersAsync(page, pageSize);
        }
    }
}
