using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Persistence.Sql.Repositories
{
    /// <summary>
    /// ADO.NET implementation of user repository
    /// </summary>
    public class UserRepository : IUserRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<UserRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the UserRepository
        /// </summary>
        /// <param name="connectionFactory">SQL connection factory</param>
        /// <param name="logger">Logger</param>
        public UserRepository(ISqlConnectionFactory connectionFactory, ILogger<UserRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a new user in the database
        /// </summary>
        /// <param name="user">User to create</param>
        /// <returns>Created user with ID</returns>
        public async Task<User> CreateUserAsync(User user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            const string sql = @"
                INSERT INTO [dbo].[Users] (
                    [Id], [Email], [DisplayName], [FirstName], [LastName], [IsEnabled], 
                    [CreatedDate], [LastUpdatedDate], [PlanId], [AuthProvider], [ProviderUserId]
                )
                VALUES (
                    @Id, @Email, @DisplayName, @FirstName, @LastName, @IsEnabled,
                    @CreatedDate, @LastUpdatedDate, @PlanId, @AuthProvider, @ProviderUserId
                );
                SELECT * FROM [dbo].[Users] WHERE [Id] = @Id;
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var createdUser = await connection.QueryFirstOrDefaultAsync<User>(sql, user);
                
                _logger.LogInformation("Created user with ID {UserId}", createdUser?.Id);
                return createdUser ?? throw new InvalidOperationException("User creation failed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create user");
                throw;
            }
        }

        /// <summary>
        /// Gets a user by their ID
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>User if found, null otherwise</returns>
        public async Task<User?> GetUserByIdAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            const string sql = @"
                SELECT * FROM [dbo].[Users] WHERE [Id] = @UserId;
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                return await connection.QueryFirstOrDefaultAsync<User>(sql, new { UserId = userId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user by ID {UserId}", userId);
                throw;
            }
        }

        /// <summary>
        /// Gets a user by their email
        /// </summary>
        /// <param name="email">User email</param>
        /// <returns>User if found, null otherwise</returns>
        public async Task<User?> GetUserByEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email cannot be null or empty", nameof(email));
            }

            const string sql = @"
                SELECT * FROM [dbo].[Users] WHERE [Email] = @Email;
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                return await connection.QueryFirstOrDefaultAsync<User>(sql, new { Email = email });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user by email {Email}", email);
                throw;
            }
        }

        /// <summary>
        /// Updates an existing user
        /// </summary>
        /// <param name="user">User to update</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> UpdateUserAsync(User user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            const string sql = @"
                UPDATE [dbo].[Users] SET
                    [Email] = @Email,
                    [DisplayName] = @DisplayName,
                    [FirstName] = @FirstName,
                    [LastName] = @LastName,
                    [IsEnabled] = @IsEnabled,
                    [LastUpdatedDate] = @LastUpdatedDate,
                    [PlanId] = @PlanId,
                    [AuthProvider] = @AuthProvider,
                    [ProviderUserId] = @ProviderUserId
                WHERE [Id] = @Id;
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                int rowsAffected = await connection.ExecuteAsync(sql, user);
                
                _logger.LogInformation("Updated user with ID {UserId}, rows affected: {RowsAffected}", user.Id, rowsAffected);
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update user with ID {UserId}", user.Id);
                throw;
            }
        }

        /// <summary>
        /// Enables or disables a user account
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="isEnabled">Enable/disable status</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> SetUserEnabledStatusAsync(string userId, bool isEnabled)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            const string sql = @"
                UPDATE [dbo].[Users] SET
                    [IsEnabled] = @IsEnabled,
                    [LastUpdatedDate] = @LastUpdatedDate
                WHERE [Id] = @UserId;
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                int rowsAffected = await connection.ExecuteAsync(sql, new 
                {
                    UserId = userId,
                    IsEnabled = isEnabled,
                    LastUpdatedDate = DateTime.UtcNow
                });
                
                _logger.LogInformation("Set user {UserId} enabled status to {IsEnabled}, rows affected: {RowsAffected}", 
                    userId, isEnabled, rowsAffected);
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set enabled status for user {UserId}", userId);
                throw;
            }
        }
    }
}
