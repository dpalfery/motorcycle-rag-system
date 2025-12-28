using System.Threading.Tasks;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Contracts.Interfaces
{
    /// <summary>
    /// Service interface for administrative user management operations
    /// </summary>
    public interface IUserAdminService
    {
        /// <summary>
        /// Enables or disables a user account
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="isEnabled">Enable/disable status</param>
        /// <returns>Updated user</returns>
        Task<User> SetUserEnabledStatusAsync(string userId, bool isEnabled);

        /// <summary>
        /// Assigns a plan to a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="planId">Plan ID to assign</param>
        /// <returns>Updated user</returns>
        Task<User> AssignPlanToUserAsync(string userId, string planId);

        /// <summary>
        /// Gets all users (admin view)
        /// </summary>
        /// <param name="page">Page number (default: 1)</param>
        /// <param name="pageSize">Page size (default: 50)</param>
        /// <returns>Paged list of users</returns>
        Task<User[]> GetAllUsersAsync(int page = 1, int pageSize = 50);
    }
}