using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models.DTOs;


namespace MotorcycleRAG.Contracts.Interfaces {
    /// <summary>
    /// Service interface for plan policy enforcement and limit checking
    /// </summary>
    public interface IPlanPolicyService {
        /// <summary>
        /// Gets the daily request limit for a user's plan
        /// </summary>
        /// <param name="user">User to check</param>
        /// <returns>Daily request limit</returns>
        Task<int> GetDailyRequestLimitAsync(UserDTO user);

        /// <summary>
        /// Checks if a user has exceeded their daily request limit
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="date">Date to check (defaults to today)</param>
        /// <returns>True if limit exceeded, false otherwise</returns>
        Task<bool> HasExceededDailyLimitAsync(string userId, DateTime? date = null);

        /// <summary>
        /// Gets the current daily usage count for a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="date">Date to check (defaults to today)</param>
        /// <returns>Current daily usage count</returns>
        Task<int> GetDailyUsageCountAsync(string userId, DateTime? date = null);

        /// <summary>
        /// Gets the remaining daily requests for a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="date">Date to check (defaults to today)</param>
        /// <returns>Remaining requests</returns>
        Task<int> GetRemainingDailyRequestsAsync(string userId, DateTime? date = null);
    }
}
