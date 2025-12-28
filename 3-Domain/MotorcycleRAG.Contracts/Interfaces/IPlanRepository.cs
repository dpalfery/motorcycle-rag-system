using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Contracts.Interfaces
{
    /// <summary>
    /// Repository interface for user plan data persistence operations
    /// </summary>
    public interface IPlanRepository
    {
        /// <summary>
        /// Creates a new plan in the database
        /// </summary>
        /// <param name="plan">Plan to create</param>
        /// <returns>Created plan with ID</returns>
        Task<UserPlan> CreatePlanAsync(UserPlan plan);

        /// <summary>
        /// Gets a plan by its ID
        /// </summary>
        /// <param name="planId">Plan ID</param>
        /// <returns>Plan if found, null otherwise</returns>
        Task<UserPlan?> GetPlanByIdAsync(string planId);

        /// <summary>
        /// Gets a plan by its name
        /// </summary>
        /// <param name="planName">Plan name</param>
        /// <returns>Plan if found, null otherwise</returns>
        Task<UserPlan?> GetPlanByNameAsync(string planName);

        /// <summary>
        /// Gets all plans
        /// </summary>
        /// <returns>List of all plans</returns>
        Task<UserPlan[]> GetAllPlansAsync();

        /// <summary>
        /// Updates an existing plan
        /// </summary>
        /// <param name="plan">Plan to update</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> UpdatePlanAsync(UserPlan plan);

        /// <summary>
        /// Deletes a plan by ID
        /// </summary>
        /// <param name="planId">Plan ID</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> DeletePlanAsync(string planId);
    }
}
