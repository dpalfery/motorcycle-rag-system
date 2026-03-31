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
    /// ADO.NET implementation of user repository
    /// </summary>
    public class UserRepository : IUserRepository {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<UserRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the UserRepository
        /// </summary>
        /// <param name="connectionFactory">SQL connection factory</param>
        /// <param name="logger">Logger</param>
        public UserRepository(ISqlConnectionFactory connectionFactory, ILogger<UserRepository> logger) {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a new user in the database
        /// </summary>
        /// <param name="user">User to create</param>
        /// <returns>Created user with ID</returns>
        public async Task<UserDTO> CreateUserAsync(UserDTO user) {
            ArgumentNullException.ThrowIfNull(user);

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

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var createdUser = await connection.QueryFirstOrDefaultAsync<UserDTO>(sql, user);

                _logger.LogInformation("Created user with ID {UserId}", createdUser?.Id);
                return createdUser ?? throw new InvalidOperationException("User creation failed");
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to create user");
                throw new InvalidOperationException("Failed to create user", ex);
            }
        }

        /// <summary>
        /// Gets a user by their ID
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>User if found, null otherwise</returns>
        public async Task<UserDTO?> GetUserByIdAsync(string userId) {
            if (string.IsNullOrWhiteSpace(userId)) {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            const string sql = @"
                SELECT * FROM [dbo].[Users] WHERE [Id] = @UserId;
            ";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                return await connection.QueryFirstOrDefaultAsync<UserDTO>(sql, new { UserId = userId });
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to get user by ID {UserId}", userId);
                throw new InvalidOperationException($"Failed to get user by ID {userId}", ex);
            }
        }

        /// <summary>
        /// Gets a user by their email
        /// </summary>
        /// <param name="email">User email</param>
        /// <returns>User if found, null otherwise</returns>
        public async Task<UserDTO?> GetUserByEmailAsync(string email) {
            if (string.IsNullOrWhiteSpace(email)) {
                throw new ArgumentException("Email cannot be null or empty", nameof(email));
            }

            const string sql = @"
                SELECT * FROM [dbo].[Users] WHERE [Email] = @Email;
            ";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                return await connection.QueryFirstOrDefaultAsync<UserDTO>(sql, new { Email = email });
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to get user by email {Email}", email);
                throw new InvalidOperationException($"Failed to get user by email {email}", ex);
            }
        }

        /// <summary>
        /// Updates an existing user
        /// </summary>
        /// <param name="user">User to update</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> UpdateUserAsync(UserDTO user) {
            ArgumentNullException.ThrowIfNull(user);

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

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                int rowsAffected = await connection.ExecuteAsync(sql, user);

                _logger.LogInformation("Updated user with ID {UserId}, rows affected: {RowsAffected}", user.Id, rowsAffected);
                return rowsAffected > 0;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to update user with ID {UserId}", user.Id);
                throw new InvalidOperationException($"Failed to update user with ID {user.Id}", ex);
            }
        }

        /// <summary>
        /// Enables or disables a user account
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="isEnabled">Enable/disable status</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> SetUserEnabledStatusAsync(string userId, bool isEnabled) {
            if (string.IsNullOrWhiteSpace(userId)) {
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
            }

            const string sql = @"
                UPDATE [dbo].[Users] SET
                    [IsEnabled] = @IsEnabled,
                    [LastUpdatedDate] = @LastUpdatedDate
                WHERE [Id] = @UserId;
            ";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                int rowsAffected = await connection.ExecuteAsync(sql, new {
                    UserId = userId,
                    IsEnabled = isEnabled,
                    LastUpdatedDate = DateTime.UtcNow
                });

                _logger.LogInformation("Set user {UserId} enabled status to {IsEnabled}, rows affected: {RowsAffected}",
                    userId, isEnabled, rowsAffected);
                return rowsAffected > 0;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to set enabled status for user {UserId}", userId);
                throw new InvalidOperationException($"Failed to set enabled status for user {userId}", ex);
            }
        }

        /// <summary>
        /// Gets a paged set of users for administrative views.
        /// </summary>
        public async Task<UserDTO[]> GetUsersAsync(int page, int pageSize) {
            if (page < 1) {
                throw new ArgumentException("Page number must be at least 1", nameof(page));
            }

            if (pageSize < 1) {
                throw new ArgumentException("Page size must be at least 1", nameof(pageSize));
            }

            const string sql = @"
                SELECT *
                FROM [dbo].[Users]
                ORDER BY [CreatedDate] DESC, [Id] ASC
                OFFSET @Offset ROWS
                FETCH NEXT @PageSize ROWS ONLY;
            ";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var offset = (page - 1) * pageSize;
                var users = await connection.QueryAsync<UserDTO>(sql, new {
                    Offset = offset,
                    PageSize = pageSize
                });

                return users.ToArray();
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to get users for page {Page} with page size {PageSize}", page, pageSize);
                throw new InvalidOperationException($"Failed to get users for page {page}", ex);
            }
        }
    }
}
