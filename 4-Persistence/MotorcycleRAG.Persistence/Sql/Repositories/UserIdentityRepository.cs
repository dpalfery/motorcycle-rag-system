using System;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Utilities;


namespace MotorcycleRAG.Persistence.Sql.Repositories {
    /// <summary>
    /// ADO.NET implementation of managed-user identity link persistence.
    /// </summary>
    public class UserIdentityRepository : IUserIdentityRepository {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<UserIdentityRepository> _logger;

        public UserIdentityRepository(ISqlConnectionFactory connectionFactory, ILogger<UserIdentityRepository> logger) {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string?> GetManagedUserIdAsync(string issuer, string subject, string email, IdentityProvider provider) {
            if (string.IsNullOrWhiteSpace(email)) {
                throw new ArgumentException("Email cannot be null or empty", nameof(email));
            }

            const string sql = @"
                SELECT TOP (1) [ManagedUserId]
                FROM [dbo].[UserIdentities]
                WHERE [Provider] = @Provider
                  AND [AccessRevokedAtUtc] IS NULL
                  AND (([Issuer] = @Issuer AND [Subject] = @Subject)
                       OR [ProviderEmail] = @Email)
                ORDER BY [LastSyncedAtUtc] DESC, [InvitationCreatedAtUtc] DESC;";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                return await connection.ExecuteScalarAsync<string?>(sql, new {
                    Issuer = string.IsNullOrWhiteSpace(issuer) ? null : issuer,
                    Subject = string.IsNullOrWhiteSpace(subject) ? null : subject,
                    Email = email,
                    Provider = provider.ToString()
                });
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to resolve managed user identity for {Provider}/{Email}",
                    provider, PiiMasking.MaskEmailForLog(email));  // codeql[cs/exposure-of-sensitive-information]
                throw new InvalidOperationException($"Failed to resolve identity for {email}", ex);
            }
        }

        public async Task<UserIdentityLinkRecord?> GetActiveByManagedUserIdAsync(string managedUserId) {
            if (string.IsNullOrWhiteSpace(managedUserId)) {
                throw new ArgumentException("Managed user ID cannot be null or empty", nameof(managedUserId));
            }

            const string sql = @"
                SELECT TOP (1)
                    [ManagedUserId],
                    [Provider],
                    [ProviderEmail],
                    [ExternalDirectoryObjectId]
                FROM [dbo].[UserIdentities]
                WHERE [ManagedUserId] = @ManagedUserId
                  AND [AccessRevokedAtUtc] IS NULL
                ORDER BY [LastSyncedAtUtc] DESC, [InvitationCreatedAtUtc] DESC;";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var row = await connection.QueryFirstOrDefaultAsync(sql, new { ManagedUserId = managedUserId });
                return row is null
                    ? null
                    : new UserIdentityLinkRecord {
                        ManagedUserId = row.ManagedUserId,
                        Provider = Enum.Parse<IdentityProvider>((string)row.Provider, ignoreCase: true),
                        ProviderEmail = row.ProviderEmail,
                        ExternalDirectoryObjectId = row.ExternalDirectoryObjectId
                    };
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to get active identity link for managed user {ManagedUserId}",
                    managedUserId);  // codeql[cs/log-forging]
                throw new InvalidOperationException($"Failed to get identity link for {managedUserId}", ex);
            }
        }

        public async Task<bool> ExistsAsync(string managedUserId, IdentityProvider provider, string providerEmail) {
            if (string.IsNullOrWhiteSpace(managedUserId)) {
                throw new ArgumentException("Managed user ID cannot be null or empty", nameof(managedUserId));
            }

            if (string.IsNullOrWhiteSpace(providerEmail)) {
                throw new ArgumentException("Provider email cannot be null or empty", nameof(providerEmail));
            }

            const string sql = @"
                SELECT COUNT(1)
                FROM [dbo].[UserIdentities]
                WHERE [ManagedUserId] = @ManagedUserId
                  AND [Provider] = @Provider
                  AND [ProviderEmail] = @ProviderEmail
                  AND [AccessRevokedAtUtc] IS NULL;";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var count = await connection.ExecuteScalarAsync<int>(sql, new {
                    ManagedUserId = managedUserId,
                    Provider = provider.ToString(),
                    ProviderEmail = providerEmail
                });

                return count > 0;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to check identity link for {ManagedUserId}",
                    managedUserId);
                throw new InvalidOperationException($"Failed to check identity link for {managedUserId}", ex);
            }
        }

        public async Task<bool> UpsertAsync(
            string managedUserId,
            IdentityProvider provider,
            string providerEmail,
            string? issuer,
            string? subject,
            string? providerUserId,
            string? externalDirectoryObjectId) {
            if (string.IsNullOrWhiteSpace(managedUserId)) {
                throw new ArgumentException("Managed user ID cannot be null or empty", nameof(managedUserId));
            }

            if (string.IsNullOrWhiteSpace(providerEmail)) {
                throw new ArgumentException("Provider email cannot be null or empty", nameof(providerEmail));
            }

            const string sql = @"
                IF EXISTS (
                    SELECT 1
                    FROM [dbo].[UserIdentities]
                    WHERE [ManagedUserId] = @ManagedUserId
                      AND [Provider] = @Provider
                      AND [ProviderEmail] = @ProviderEmail
                )
                BEGIN
                    UPDATE [dbo].[UserIdentities]
                    SET [Issuer] = COALESCE(@Issuer, [Issuer]),
                        [Subject] = COALESCE(@Subject, [Subject]),
                        [ProviderUserId] = COALESCE(@ProviderUserId, [ProviderUserId]),
                        [ExternalDirectoryObjectId] = COALESCE(@ExternalDirectoryObjectId, [ExternalDirectoryObjectId]),
                        [InvitationStatus] = CASE WHEN @ExternalDirectoryObjectId IS NULL THEN [InvitationStatus] ELSE N'Provisioned' END,
                        [InvitationCreatedAtUtc] = COALESCE([InvitationCreatedAtUtc], CASE WHEN @ExternalDirectoryObjectId IS NULL THEN NULL ELSE SYSUTCDATETIME() END),
                        [AccessRevokedAtUtc] = NULL,
                        [LastSyncedAtUtc] = SYSUTCDATETIME()
                    WHERE [ManagedUserId] = @ManagedUserId
                      AND [Provider] = @Provider
                      AND [ProviderEmail] = @ProviderEmail;
                END
                ELSE
                BEGIN
                    INSERT INTO [dbo].[UserIdentities] (
                        [ManagedUserId],
                        [Provider],
                        [ProviderEmail],
                        [Issuer],
                        [Subject],
                        [ProviderUserId],
                        [ExternalDirectoryObjectId],
                        [InvitationStatus],
                        [InvitationCreatedAtUtc],
                        [LastSyncedAtUtc]
                    )
                    VALUES (
                        @ManagedUserId,
                        @Provider,
                        @ProviderEmail,
                        @Issuer,
                        @Subject,
                        @ProviderUserId,
                        @ExternalDirectoryObjectId,
                        CASE WHEN @ExternalDirectoryObjectId IS NULL THEN N'NotCreated' ELSE N'Provisioned' END,
                        CASE WHEN @ExternalDirectoryObjectId IS NULL THEN NULL ELSE SYSUTCDATETIME() END,
                        SYSUTCDATETIME()
                    );
                END;";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var rowsAffected = await connection.ExecuteAsync(sql, new {
                    ManagedUserId = managedUserId,
                    Provider = provider.ToString(),
                    ProviderEmail = providerEmail,
                    Issuer = issuer,
                    Subject = subject,
                    ProviderUserId = providerUserId,
                    ExternalDirectoryObjectId = externalDirectoryObjectId
                });

                return rowsAffected > 0;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to upsert identity link for {ManagedUserId}",
                    managedUserId);
                throw new InvalidOperationException($"Failed to upsert identity link for {managedUserId}", ex);
            }
        }

        public async Task<bool> MarkAccessRevokedAsync(string managedUserId) {
            if (string.IsNullOrWhiteSpace(managedUserId)) {
                throw new ArgumentException("Managed user ID cannot be null or empty", nameof(managedUserId));
            }

            const string sql = @"
                UPDATE [dbo].[UserIdentities]
                SET [AccessRevokedAtUtc] = COALESCE([AccessRevokedAtUtc], SYSUTCDATETIME()),
                    [LastSyncedAtUtc] = SYSUTCDATETIME()
                WHERE [ManagedUserId] = @ManagedUserId
                  AND [AccessRevokedAtUtc] IS NULL;";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var rowsAffected = await connection.ExecuteAsync(sql, new { ManagedUserId = managedUserId });
                return rowsAffected > 0;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to revoke identity links for managed user {ManagedUserId}",
                    managedUserId);  // codeql[cs/log-forging]
                throw new InvalidOperationException($"Failed to revoke identity links for {managedUserId}", ex);
            }
        }
    }
}
