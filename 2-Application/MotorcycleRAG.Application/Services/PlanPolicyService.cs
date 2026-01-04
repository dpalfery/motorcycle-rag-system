using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;


namespace MotorcycleRAG.Application.Services
{
    /// <summary>
    /// Service for enforcing plan policies and checking daily request limits
    /// </summary>
    public class PlanPolicyService : IPlanPolicyService
    {
        private readonly IPlanRepository _planRepository;
        private readonly IUsageRepository _usageRepository;
        private readonly IUserRepository _userRepository;
        private readonly ILogger<PlanPolicyService> _logger;

        /// <summary>
        /// Initializes a new instance of PlanPolicyService
        /// </summary>
        /// <param name="planRepository">Plan repository</param>
        /// <param name="usageRepository">Usage repository</param>
        /// <param name="userRepository">User repository</param>
        /// <param name="logger">Logger</param>
        public PlanPolicyService(
            IPlanRepository planRepository,
            IUsageRepository usageRepository,
            IUserRepository userRepository,
            ILogger<PlanPolicyService> logger)
        {
            _planRepository = planRepository ?? throw new ArgumentNullException(nameof(planRepository));
            _usageRepository = usageRepository ?? throw new ArgumentNullException(nameof(usageRepository));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets daily request limit for a user's plan
        /// </summary>
        /// <param name="user">User to check</param>
        /// <returns>Daily request limit</returns>
        public async Task<int> GetDailyRequestLimitAsync(UserDTO user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            // If user has no plan assigned, return default limit
            if (string.IsNullOrWhiteSpace(user.PlanId))
            {
                _logger.LogWarning("User {UserId} has no plan assigned, using default limit", user.Id);
                return 100; // Default free tier limit
            }

            try
            {
                var plan = await _planRepository.GetPlanByIdAsync(user.PlanId);
                if (plan == null)
                {
                    _logger.LogWarning("User {UserId} has unknown plan {PlanId}, using default limit", user.Id, user.PlanId);
                    return 100; // Default free tier limit
                }

                _logger.LogDebug("User {UserId} has plan {PlanName} with daily limit {DailyLimit}",
                    user.Id, plan.Name, plan.DailyRequestLimit);
                return plan.DailyRequestLimit;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting daily request limit for user {UserId}", user.Id);
                return 100; // Default free tier limit on error
            }
        }

        /// <summary>
        /// Checks if a user has exceeded their daily request limit
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="date">Date to check (defaults to today)</param>
        /// <returns>True if limit exceeded, false otherwise</returns>
        public async Task<bool> HasExceededDailyLimitAsync(string userId, DateTime? date = null)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            var checkDate = date ?? DateTime.UtcNow;
            var dailyCount = await GetDailyUsageCountAsync(userId, checkDate);
            var limit = await GetDailyLimitForUserAsync(userId);

            var hasExceeded = dailyCount >= limit;

            if (hasExceeded)
            {
                _logger.LogWarning("User {UserId} has exceeded daily limit: {Count}/{Limit}", userId, dailyCount, limit);
            }

            return hasExceeded;
        }

        /// <summary>
        /// Gets current daily usage count for a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="date">Date to check (defaults to today)</param>
        /// <returns>Current daily usage count</returns>
        public async Task<int> GetDailyUsageCountAsync(string userId, DateTime? date = null)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            var checkDate = date ?? DateTime.UtcNow;

            try
            {
                var count = await _usageRepository.GetDailyUsageCountAsync(userId, checkDate);
                _logger.LogDebug("User {UserId} has made {Count} requests on {Date}", userId, count, checkDate.Date);
                return count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting daily usage count for user {UserId}", userId);
                return 0; // Return 0 on error to allow requests
            }
        }

        /// <summary>
        /// Gets remaining daily requests for a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="date">Date to check (defaults to today)</param>
        /// <returns>Remaining requests</returns>
        public async Task<int> GetRemainingDailyRequestsAsync(string userId, DateTime? date = null)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            var dailyCount = await GetDailyUsageCountAsync(userId, date);
            var limit = await GetDailyLimitForUserAsync(userId);
            var remaining = Math.Max(0, limit - dailyCount);

            _logger.LogDebug("User {UserId} has {Remaining} requests remaining today", userId, remaining);
            return remaining;
        }

        /// <summary>
        /// Helper method to get daily limit for a user by ID
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>Daily request limit</returns>
        private async Task<int> GetDailyLimitForUserAsync(string userId)
        {
            try
            {
                var user = await _userRepository.GetUserByIdAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found, using default limit", userId);
                    return 100; // Default free tier limit
                }

                return await GetDailyRequestLimitAsync(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting daily limit for user {UserId}", userId);
                return 100; // Default free tier limit on error
            }
        }
    }
}
