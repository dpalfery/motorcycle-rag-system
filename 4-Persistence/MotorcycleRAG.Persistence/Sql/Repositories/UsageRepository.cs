using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Persistence.Sql.Repositories
{
    /// <summary>
    /// ADO.NET implementation of usage repository
    /// </summary>
    public class UsageRepository : IUsageRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<UsageRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the UsageRepository
        /// </summary>
        /// <param name="connectionFactory">SQL connection factory</param>
        /// <param name="logger">Logger</param>
        public UsageRepository(ISqlConnectionFactory connectionFactory, ILogger<UsageRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Records a user's API usage
        /// </summary>
        /// <param name="usage">Usage record to create</param>
        /// <returns>Created usage record</returns>
        public async Task<Usage> RecordUsageAsync(Usage usage)
        {
            if (usage == null)
            {
                throw new ArgumentNullException(nameof(usage));
            }

            const string sql = @"
                INSERT INTO [dbo].[Usage] (
                    [UserId], [Endpoint], [HttpMethod], [QueryId], [RequestTime], 
                    [DurationMs], [StatusCode], [IsSuccess], [CallerIp], [UserAgent]
                )
                VALUES (
                    @UserId, @Endpoint, @HttpMethod, @QueryId, @RequestTime,
                    @DurationMs, @StatusCode, @IsSuccess, @CallerIp, @UserAgent
                );
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT) AS [Id];
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                using var transaction = connection.BeginTransaction();
                
                try
                {
                    var usageId = await connection.QueryFirstOrDefaultAsync<long>(sql, usage, transaction);
                    usage.Id = usageId;
                    
                    transaction.Commit();
                    _logger.LogDebug("Recorded usage with ID {UsageId} for user {UserId}", usageId, usage.UserId);
                    return usage;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record usage for user {UserId}", usage.UserId);
                throw;
            }
        }

        /// <summary>
        /// Gets usage statistics for a user within a date range
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="startDate">Start date</param>
        /// <param name="endDate">End date</param>
        /// <returns>List of usage records</returns>
        public async Task<Usage[]> GetUsageByUserAndDateRangeAsync(string userId, DateTime startDate, DateTime endDate)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            const string sql = @"
                SELECT * 
                FROM [dbo].[Usage]
                WHERE [UserId] = @UserId
                AND [RequestTime] BETWEEN @StartDate AND @EndDate
                ORDER BY [RequestTime] DESC;
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                return (await connection.QueryAsync<Usage>(sql, new 
                {
                    UserId = userId,
                    StartDate = startDate,
                    EndDate = endDate
                })).ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get usage for user {UserId} from {StartDate} to {EndDate}", 
                    userId, startDate, endDate);
                throw;
            }
        }

        /// <summary>
        /// Gets daily usage count for a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="date">Date to check</param>
        /// <returns>Count of usage records for the day</returns>
        public async Task<int> GetDailyUsageCountAsync(string userId, DateTime date)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            const string sql = @"
                EXEC [dbo].[sp_GetDailyUsageCount] 
                    @UserId = @UserId,
                    @Date = @Date;
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var result = await connection.QueryFirstOrDefaultAsync<int>(sql, new 
                {
                    UserId = userId,
                    Date = date.Date
                });
                
                _logger.LogDebug("Daily usage count for user {UserId} on {Date}: {Count}", userId, date.Date, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get daily usage count for user {UserId} on {Date}", userId, date.Date);
                throw;
            }
        }
    }
}