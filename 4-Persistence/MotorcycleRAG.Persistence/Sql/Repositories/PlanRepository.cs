using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Enums;


namespace MotorcycleRAG.Persistence.Sql.Repositories {
    /// <summary>
    /// ADO.NET implementation of plan repository
    /// </summary>
    public class PlanRepository : IPlanRepository {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<PlanRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the PlanRepository
        /// </summary>
        /// <param name="connectionFactory">SQL connection factory</param>
        /// <param name="logger">Logger</param>
        public PlanRepository(ISqlConnectionFactory connectionFactory, ILogger<PlanRepository> logger) {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a new plan in the database
        /// </summary>
        /// <param name="plan">Plan to create</param>
        /// <returns>Created plan with ID</returns>
        public async Task<UserPlan> CreatePlanAsync(UserPlan plan) {
            ArgumentNullException.ThrowIfNull(plan);

            const string sql = @"
                INSERT INTO [dbo].[UserPlans] (
                    [Id], [Name], [Description], [DailyRequestLimit], [IsPaid], [CreatedDate]
                )
                VALUES (
                    @Id, @Name, @Description, @DailyRequestLimit, @IsPaid, @CreatedDate
                );
                SELECT * FROM [dbo].[UserPlans] WHERE [Id] = @Id;
            ";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var createdPlan = await connection.QueryFirstOrDefaultAsync<UserPlan>(sql, plan);

                _logger.LogInformation("Created plan with ID {PlanId}", createdPlan?.Id);
                return createdPlan ?? throw new InvalidOperationException("Plan creation failed");
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to create plan");
                throw new InvalidOperationException("Failed to create plan", ex);
            }
        }

        /// <summary>
        /// Gets a plan by its ID
        /// </summary>
        /// <param name="planId">Plan ID</param>
        /// <returns>Plan if found, null otherwise</returns>
        public async Task<UserPlan?> GetPlanByIdAsync(string planId) {
            if (string.IsNullOrWhiteSpace(planId)) {
                throw new ArgumentException("Plan ID cannot be null or empty", nameof(planId));
            }

            const string sql = @"
                SELECT * FROM [dbo].[UserPlans] WHERE [Id] = @PlanId;
            ";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                return await connection.QueryFirstOrDefaultAsync<UserPlan>(sql, new { PlanId = planId });
            }
            catch (Exception ex) {
                // codeql[cs/log-forging]
                _logger.LogError(ex, "Failed to get plan by ID {PlanId}", planId);
                throw new InvalidOperationException($"Failed to get plan by ID {planId}", ex);
            }
        }

        /// <summary>
        /// Gets a plan by its name
        /// </summary>
        /// <param name="planName">Plan name</param>
        /// <returns>Plan if found, null otherwise</returns>
        public async Task<UserPlan?> GetPlanByNameAsync(string planName) {
            if (string.IsNullOrWhiteSpace(planName)) {
                throw new ArgumentException("Plan name cannot be null or empty", nameof(planName));
            }

            const string sql = @"
                SELECT * FROM [dbo].[UserPlans] WHERE [Name] = @PlanName;
            ";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                return await connection.QueryFirstOrDefaultAsync<UserPlan>(sql, new { PlanName = planName });
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to get plan by name {PlanName}", planName);
                throw new InvalidOperationException($"Failed to get plan by name {planName}", ex);
            }
        }

        /// <summary>
        /// Gets all plans
        /// </summary>
        /// <returns>List of all plans</returns>
        public async Task<UserPlan[]> GetAllPlansAsync() {
            const string sql = @"
                SELECT * FROM [dbo].[UserPlans] ORDER BY [IsPaid] DESC, [Name];
            ";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var plans = await connection.QueryAsync<UserPlan>(sql);
                return plans.ToArray();
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to get all plans");
                throw new InvalidOperationException("Failed to get all plans", ex);
            }
        }

        /// <summary>
        /// Updates an existing plan
        /// </summary>
        /// <param name="plan">Plan to update</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> UpdatePlanAsync(UserPlan plan) {
            ArgumentNullException.ThrowIfNull(plan);

            const string sql = @"
                UPDATE [dbo].[UserPlans] SET
                    [Name] = @Name,
                    [Description] = @Description,
                    [DailyRequestLimit] = @DailyRequestLimit,
                    [IsPaid] = @IsPaid
                WHERE [Id] = @Id;
            ";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                int rowsAffected = await connection.ExecuteAsync(sql, plan);

                _logger.LogInformation("Updated plan with ID {PlanId}, rows affected: {RowsAffected}",
                    plan.Id, rowsAffected);
                return rowsAffected > 0;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to update plan with ID {PlanId}", plan.Id);
                throw new InvalidOperationException($"Failed to update plan with ID {plan.Id}", ex);
            }
        }

        /// <summary>
        /// Deletes a plan by ID
        /// </summary>
        /// <param name="planId">Plan ID</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> DeletePlanAsync(string planId) {
            if (string.IsNullOrWhiteSpace(planId)) {
                throw new ArgumentException("Plan ID cannot be null or empty", nameof(planId));
            }

            const string sql = @"
                DELETE FROM [dbo].[UserPlans] WHERE [Id] = @PlanId;
            ";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                int rowsAffected = await connection.ExecuteAsync(sql, new { PlanId = planId });

                _logger.LogInformation("Deleted plan with ID {PlanId}, rows affected: {RowsAffected}",
                    // codeql[cs/log-forging]
                    planId, rowsAffected);
                return rowsAffected > 0;
            }
            catch (Exception ex) {
                // codeql[cs/log-forging]
                _logger.LogError(ex, "Failed to delete plan with ID {PlanId}", planId);
                throw new InvalidOperationException($"Failed to delete plan with ID {planId}", ex);
            }
        }
    }
}
