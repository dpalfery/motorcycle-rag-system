using System;
using System.Threading.Tasks;
using MotorcycleRAG.Domain.DTOs;


namespace MotorcycleRAG.Contracts.Interfaces
{
    /// <summary>
    /// Service interface for tracking API usage
    /// </summary>
    public interface IUsageTrackingService
    {
        /// <summary>
        /// Records a usage event
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="endpoint">API endpoint</param>
        /// <param name="httpMethod">HTTP method</param>
        /// <param name="queryId">Query ID (optional)</param>
        /// <param name="durationMs">Duration in milliseconds</param>
        /// <param name="statusCode">HTTP status code</param>
        /// <param name="callerIp">Caller IP address (optional)</param>
        /// <param name="userAgent">User agent string (optional)</param>
        /// <returns>Recorded usage</returns>
        Task<Usage> RecordUsageAsync(
            string userId,
            string endpoint,
            string httpMethod,
            string? queryId = null,
            long durationMs = 0,
            int statusCode = 200,
            string? callerIp = null,
            string? userAgent = null);

        /// <summary>
        /// Records a successful usage event
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="endpoint">API endpoint</param>
        /// <param name="httpMethod">HTTP method</param>
        /// <param name="queryId">Query ID (optional)</param>
        /// <param name="durationMs">Duration in milliseconds</param>
        /// <param name="callerIp">Caller IP address (optional)</param>
        /// <param name="userAgent">User agent string (optional)</param>
        /// <returns>Recorded usage</returns>
        Task<Usage> RecordSuccessAsync(
            string userId,
            string endpoint,
            string httpMethod,
            string? queryId = null,
            long durationMs = 0,
            string? callerIp = null,
            string? userAgent = null);

        /// <summary>
        /// Records a failed usage event
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="endpoint">API endpoint</param>
        /// <param name="httpMethod">HTTP method</param>
        /// <param name="statusCode">HTTP status code</param>
        /// <param name="queryId">Query ID (optional)</param>
        /// <param name="durationMs">Duration in milliseconds</param>
        /// <param name="callerIp">Caller IP address (optional)</param>
        /// <param name="userAgent">User agent string (optional)</param>
        /// <returns>Recorded usage</returns>
        Task<Usage> RecordFailureAsync(
            string userId,
            string endpoint,
            string httpMethod,
            int statusCode,
            string? queryId = null,
            long durationMs = 0,
            string? callerIp = null,
            string? userAgent = null);

        /// <summary>
        /// Gets usage statistics for a user within a date range
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="startDate">Start date</param>
        /// <param name="endDate">End date</param>
        /// <returns>List of usage records</returns>
        Task<Usage[]> GetUsageByDateRangeAsync(
            string userId,
            DateTime startDate,
            DateTime endDate);
    }
}
