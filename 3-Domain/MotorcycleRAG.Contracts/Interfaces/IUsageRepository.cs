using System;
using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models.DTOs;


namespace MotorcycleRAG.Contracts.Interfaces {
    /// <summary>
    /// Repository interface for usage tracking operations
    /// </summary>
    public interface IUsageRepository {
        /// <summary>
        /// Records a user's API usage
        /// </summary>
        /// <param name="usage">Usage record to create</param>
        /// <returns>Created usage record</returns>
        Task<Usage> RecordUsageAsync(Usage usage);

        /// <summary>
        /// Gets usage statistics for a user within a date range
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="startDate">Start date</param>
        /// <param name="endDate">End date</param>
        /// <returns>List of usage records</returns>
        Task<Usage[]> GetUsageByUserAndDateRangeAsync(string userId, DateTime startDate, DateTime endDate);

        /// <summary>
        /// Gets daily usage count for a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="date">Date to check</param>
        /// <returns>Count of usage records for the day</returns>
        Task<int> GetDailyUsageCountAsync(string userId, DateTime date);
    }
}
