using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models.DTOs;


namespace MotorcycleRAG.Contracts.Interfaces {
    /// <summary>
    /// Repository interface for user data persistence operations
    /// </summary>
    public interface IUserRepository {
        /// <summary>
        /// Creates a new user in the database
        /// </summary>
        /// <param name="user">User to create</param>
        /// <returns>Created user with ID</returns>
        Task<UserDTO> CreateUserAsync(UserDTO user);

        /// <summary>
        /// Gets a user by their ID
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>User if found, null otherwise</returns>
        Task<UserDTO?> GetUserByIdAsync(string userId);

        /// <summary>
        /// Gets a user by their email
        /// </summary>
        /// <param name="email">User email</param>
        /// <returns>User if found, null otherwise</returns>
        Task<UserDTO?> GetUserByEmailAsync(string email);

        /// <summary>
        /// Updates an existing user
        /// </summary>
        /// <param name="user">User to update</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> UpdateUserAsync(UserDTO user);

        /// <summary>
        /// Enables or disables a user account
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="isEnabled">Enable/disable status</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> SetUserEnabledStatusAsync(string userId, bool isEnabled);

        /// <summary>
        /// Assigns a plan and tier label to a managed user.
        /// </summary>
        /// <param name="userId">Managed user ID</param>
        /// <param name="planId">Mapped internal plan ID</param>
        /// <param name="tierLabel">User-facing tier label</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> AssignTierAsync(string userId, string planId, TierLabel tierLabel);

        /// <summary>
        /// Updates the effective access state for a managed user.
        /// </summary>
        /// <param name="userId">Managed user ID</param>
        /// <param name="accessState">New access state</param>
        /// <param name="isEnabled">Whether the account should remain enabled</param>
        /// <param name="cancelledByUserId">Cancelling internal user ID when applicable</param>
        /// <param name="cancelReason">Cancellation reason when applicable</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> UpdateAccessStateAsync(
            string userId,
            ManagedUserAccessState accessState,
            bool isEnabled,
            string? cancelledByUserId,
            string? cancelReason);

        /// <summary>
        /// Gets a paged set of users for administrative views
        /// </summary>
        /// <param name="page">1-based page number</param>
        /// <param name="pageSize">Number of users per page</param>
        /// <returns>Paged users ordered by creation date descending</returns>
        Task<UserDTO[]> GetUsersAsync(int page, int pageSize);
    }
}
