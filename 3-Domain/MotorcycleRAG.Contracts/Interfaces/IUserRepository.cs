using System.Threading.Tasks;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Contracts.Interfaces
{
    /// <summary>
    /// Repository interface for user data persistence operations
    /// </summary>
    public interface IUserRepository
    {
        /// <summary>
        /// Creates a new user in the database
        /// </summary>
        /// <param name="user">User to create</param>
        /// <returns>Created user with ID</returns>
        Task<User> CreateUserAsync(User user);

        /// <summary>
        /// Gets a user by their ID
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>User if found, null otherwise</returns>
        Task<User?> GetUserByIdAsync(string userId);

        /// <summary>
        /// Gets a user by their email
        /// </summary>
        /// <param name="email">User email</param>
        /// <returns>User if found, null otherwise</returns>
        Task<User?> GetUserByEmailAsync(string email);

        /// <summary>
        /// Updates an existing user
        /// </summary>
        /// <param name="user">User to update</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> UpdateUserAsync(User user);

        /// <summary>
        /// Enables or disables a user account
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="isEnabled">Enable/disable status</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> SetUserEnabledStatusAsync(string userId, bool isEnabled);
    }
}