using System;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Utilities;


namespace MotorcycleRAG.Persistence.Sql.Repositories {
    /// <summary>
    /// ADO.NET implementation of onboarding access-request persistence operations.
    /// </summary>
    public class AccessRequestRepository : IAccessRequestRepository {
        private const string AdminSelectColumns = @"
                    CONVERT(NVARCHAR(36), [AccessRequestId]) AS [RequestId],
                    [RequestedEmail] AS [Email],
                    [RequestedProvider] AS [Provider],
                    [RequestDecisionState],
                    [OnboardingExecutionState],
                    [AssignedTier],
                    [ManagedUserId],
                    [ExternalDirectoryObjectId],
                    [CorrelationId],
                    [RequestedAtUtc],
                    [ApprovedAtUtc],
                    [CancelledAtUtc],
                    [OnboardingAttemptCount],
                    [LastFailureCode],
                    [LastFailureMessage],
                    sys.fn_varbintohexstr([RowVersion]) AS [RowVersion]";

        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<AccessRequestRepository> _logger;

        public AccessRequestRepository(ISqlConnectionFactory connectionFactory, ILogger<AccessRequestRepository> logger) {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PublicAccessRequestResponse?> GetByProviderAndEmailAsync(string email, IdentityProvider provider) {
            if (string.IsNullOrWhiteSpace(email)) {
                throw new ArgumentException("Email cannot be null or empty", nameof(email));
            }

            const string sql = @"
                SELECT TOP (1)
                    CONVERT(NVARCHAR(36), [AccessRequestId]) AS [RequestId],
                    [RequestedEmail] AS [Email],
                    [RequestedProvider] AS [Provider],
                    [RequestDecisionState],
                    [OnboardingExecutionState],
                    [RequestedAtUtc],
                    [CorrelationId],
                    sys.fn_varbintohexstr([RowVersion]) AS [RowVersion]
                FROM [dbo].[AccessRequests]
                WHERE [RequestedEmail] = @Email AND [RequestedProvider] = @Provider
                ORDER BY [RequestedAtUtc] DESC;";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var row = await connection.QueryFirstOrDefaultAsync(sql, new { Email = email, Provider = provider.ToString() });
                return row is null ? null : MapPublicResponse(row);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to get access request for {Provider}/{Email}",
                    LogSanitizer.Sanitize(provider), LogSanitizer.Sanitize(email));
                throw new InvalidOperationException($"Failed to get access request for {email}", ex);
            }
        }

        public async Task<PublicAccessRequestResponse?> GetByRequestIdAsync(string requestId) {
            if (string.IsNullOrWhiteSpace(requestId)) {
                throw new ArgumentException("Request ID cannot be null or empty", nameof(requestId));
            }

            const string sql = @"
                SELECT
                    CONVERT(NVARCHAR(36), [AccessRequestId]) AS [RequestId],
                    [RequestedEmail] AS [Email],
                    [RequestedProvider] AS [Provider],
                    [RequestDecisionState],
                    [OnboardingExecutionState],
                    [RequestedAtUtc],
                    [CorrelationId],
                    sys.fn_varbintohexstr([RowVersion]) AS [RowVersion]
                FROM [dbo].[AccessRequests]
                WHERE [AccessRequestId] = @AccessRequestId;";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var row = await connection.QueryFirstOrDefaultAsync(sql, new { AccessRequestId = Guid.Parse(requestId) });
                return row is null ? null : MapPublicResponse(row);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException) {
                throw new ArgumentException("Request ID must be a valid GUID", nameof(requestId), ex);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to get access request {RequestId}", LogSanitizer.Sanitize(requestId));
                throw new InvalidOperationException($"Failed to get access request {requestId}", ex);
            }
        }

        public async Task<AccessRequestAdminRecord?> GetAdminRecordByRequestIdAsync(string requestId) {
            if (string.IsNullOrWhiteSpace(requestId)) {
                throw new ArgumentException("Request ID cannot be null or empty", nameof(requestId));
            }

            var sql = $@"
                SELECT
{AdminSelectColumns}
                FROM [dbo].[AccessRequests]
                WHERE [AccessRequestId] = @AccessRequestId;";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var row = await connection.QueryFirstOrDefaultAsync(sql, new { AccessRequestId = Guid.Parse(requestId) });
                return row is null ? null : MapAdminRecord(row);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException) {
                throw new ArgumentException("Request ID must be a valid GUID", nameof(requestId), ex);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to get admin access request {RequestId}", LogSanitizer.Sanitize(requestId));
                throw new InvalidOperationException($"Failed to get access request {requestId}", ex);
            }
        }

        public async Task<AccessRequestAdminRecord?> BeginApprovalOnboardingAsync(string requestId, TierLabel tier, string expectedRowVersion, string? approvedByUserId) {
            return await UpdateAdminRecordAsync(
                requestId,
                expectedRowVersion,
                @"
                UPDATE [dbo].[AccessRequests]
                SET [RequestDecisionState] = N'Approved',
                    [OnboardingExecutionState] = N'InProgress',
                    [AssignedTier] = @AssignedTier,
                    [ApprovedByUserId] = COALESCE(@ApprovedByUserId, [ApprovedByUserId]),
                    [ApprovedAtUtc] = COALESCE([ApprovedAtUtc], SYSUTCDATETIME()),
                    [CancelledByUserId] = NULL,
                    [CancelledAtUtc] = NULL,
                    [CancelReason] = NULL,
                    [OnboardingAttemptCount] = [OnboardingAttemptCount] + 1,
                    [LastFailureCode] = NULL,
                    [LastFailureMessage] = NULL
                OUTPUT
                    CONVERT(NVARCHAR(36), inserted.[AccessRequestId]),
                    inserted.[RequestedEmail],
                    inserted.[RequestedProvider],
                    inserted.[RequestDecisionState],
                    inserted.[OnboardingExecutionState],
                    inserted.[AssignedTier],
                    inserted.[ManagedUserId],
                    inserted.[ExternalDirectoryObjectId],
                    inserted.[CorrelationId],
                    inserted.[RequestedAtUtc],
                    inserted.[ApprovedAtUtc],
                    inserted.[CancelledAtUtc],
                    inserted.[OnboardingAttemptCount],
                    inserted.[LastFailureCode],
                    inserted.[LastFailureMessage],
                    CONVERT(NVARCHAR(260), CONVERT(VARBINARY(8), inserted.[RowVersion]), 1)
                INTO @Updated
                WHERE [AccessRequestId] = @AccessRequestId
                  AND [RowVersion] = CONVERT(VARBINARY(8), @ExpectedRowVersion, 1)
                  AND [RequestDecisionState] = N'Pending';",
                new {
                    AssignedTier = tier.ToString(),
                    ApprovedByUserId = approvedByUserId
                });
        }

        public async Task<AccessRequestAdminRecord?> RetryOnboardingAsync(string requestId, string expectedRowVersion) {
            return await UpdateAdminRecordAsync(
                requestId,
                expectedRowVersion,
                @"
                UPDATE [dbo].[AccessRequests]
                SET [OnboardingExecutionState] = N'InProgress',
                    [OnboardingAttemptCount] = [OnboardingAttemptCount] + 1,
                    [LastFailureCode] = NULL,
                    [LastFailureMessage] = NULL
                OUTPUT
                    CONVERT(NVARCHAR(36), inserted.[AccessRequestId]),
                    inserted.[RequestedEmail],
                    inserted.[RequestedProvider],
                    inserted.[RequestDecisionState],
                    inserted.[OnboardingExecutionState],
                    inserted.[AssignedTier],
                    inserted.[ManagedUserId],
                    inserted.[ExternalDirectoryObjectId],
                    inserted.[CorrelationId],
                    inserted.[RequestedAtUtc],
                    inserted.[ApprovedAtUtc],
                    inserted.[CancelledAtUtc],
                    inserted.[OnboardingAttemptCount],
                    inserted.[LastFailureCode],
                    inserted.[LastFailureMessage],
                    CONVERT(NVARCHAR(260), CONVERT(VARBINARY(8), inserted.[RowVersion]), 1)
                INTO @Updated
                WHERE [AccessRequestId] = @AccessRequestId
                  AND [RowVersion] = CONVERT(VARBINARY(8), @ExpectedRowVersion, 1)
                  AND [RequestDecisionState] = N'Approved'
                  AND [OnboardingExecutionState] = N'Failed';",
                parameters: null);
        }

        public async Task<AccessRequestAdminRecord?> CompleteOnboardingAsync(string requestId, string managedUserId, string externalDirectoryObjectId) {
            if (string.IsNullOrWhiteSpace(managedUserId)) {
                throw new ArgumentException("Managed user ID cannot be null or empty", nameof(managedUserId));
            }

            if (string.IsNullOrWhiteSpace(externalDirectoryObjectId)) {
                throw new ArgumentException("External directory object ID cannot be null or empty", nameof(externalDirectoryObjectId));
            }

            const string sql = @"
                DECLARE @Updated TABLE (
                    [RequestId] NVARCHAR(36),
                    [Email] NVARCHAR(256),
                    [Provider] NVARCHAR(50),
                    [RequestDecisionState] NVARCHAR(50),
                    [OnboardingExecutionState] NVARCHAR(50),
                    [AssignedTier] NVARCHAR(50),
                    [ManagedUserId] NVARCHAR(128),
                    [ExternalDirectoryObjectId] NVARCHAR(128),
                    [CorrelationId] NVARCHAR(128),
                    [RequestedAtUtc] DATETIME2(7),
                    [ApprovedAtUtc] DATETIME2(7),
                    [CancelledAtUtc] DATETIME2(7),
                    [OnboardingAttemptCount] INT,
                    [LastFailureCode] NVARCHAR(100),
                    [LastFailureMessage] NVARCHAR(1000),
                    [RowVersion] NVARCHAR(260)
                );

                UPDATE [dbo].[AccessRequests]
                SET [ManagedUserId] = @ManagedUserId,
                    [ExternalDirectoryObjectId] = @ExternalDirectoryObjectId,
                    [OnboardingExecutionState] = N'Completed',
                    [LastFailureCode] = NULL,
                    [LastFailureMessage] = NULL
                OUTPUT
                    CONVERT(NVARCHAR(36), inserted.[AccessRequestId]),
                    inserted.[RequestedEmail],
                    inserted.[RequestedProvider],
                    inserted.[RequestDecisionState],
                    inserted.[OnboardingExecutionState],
                    inserted.[AssignedTier],
                    inserted.[ManagedUserId],
                    inserted.[ExternalDirectoryObjectId],
                    inserted.[CorrelationId],
                    inserted.[RequestedAtUtc],
                    inserted.[ApprovedAtUtc],
                    inserted.[CancelledAtUtc],
                    inserted.[OnboardingAttemptCount],
                    inserted.[LastFailureCode],
                    inserted.[LastFailureMessage],
                    CONVERT(NVARCHAR(260), CONVERT(VARBINARY(8), inserted.[RowVersion]), 1)
                INTO @Updated
                WHERE [AccessRequestId] = @AccessRequestId;

                SELECT * FROM @Updated;";

            return await ExecuteAdminUpdateAsync(requestId, sql, new {
                ManagedUserId = managedUserId,
                ExternalDirectoryObjectId = externalDirectoryObjectId
            });
        }

        public async Task<AccessRequestAdminRecord?> FailOnboardingAsync(string requestId, string failureCode, string failureMessage, string? managedUserId, string? externalDirectoryObjectId) {
            if (string.IsNullOrWhiteSpace(failureCode)) {
                throw new ArgumentException("Failure code cannot be null or empty", nameof(failureCode));
            }

            if (string.IsNullOrWhiteSpace(failureMessage)) {
                throw new ArgumentException("Failure message cannot be null or empty", nameof(failureMessage));
            }

            const string sql = @"
                DECLARE @Updated TABLE (
                    [RequestId] NVARCHAR(36),
                    [Email] NVARCHAR(256),
                    [Provider] NVARCHAR(50),
                    [RequestDecisionState] NVARCHAR(50),
                    [OnboardingExecutionState] NVARCHAR(50),
                    [AssignedTier] NVARCHAR(50),
                    [ManagedUserId] NVARCHAR(128),
                    [ExternalDirectoryObjectId] NVARCHAR(128),
                    [CorrelationId] NVARCHAR(128),
                    [RequestedAtUtc] DATETIME2(7),
                    [ApprovedAtUtc] DATETIME2(7),
                    [CancelledAtUtc] DATETIME2(7),
                    [OnboardingAttemptCount] INT,
                    [LastFailureCode] NVARCHAR(100),
                    [LastFailureMessage] NVARCHAR(1000),
                    [RowVersion] NVARCHAR(260)
                );

                UPDATE [dbo].[AccessRequests]
                SET [ManagedUserId] = COALESCE(@ManagedUserId, [ManagedUserId]),
                    [ExternalDirectoryObjectId] = COALESCE(@ExternalDirectoryObjectId, [ExternalDirectoryObjectId]),
                    [OnboardingExecutionState] = N'Failed',
                    [LastFailureCode] = @FailureCode,
                    [LastFailureMessage] = @FailureMessage
                OUTPUT
                    CONVERT(NVARCHAR(36), inserted.[AccessRequestId]),
                    inserted.[RequestedEmail],
                    inserted.[RequestedProvider],
                    inserted.[RequestDecisionState],
                    inserted.[OnboardingExecutionState],
                    inserted.[AssignedTier],
                    inserted.[ManagedUserId],
                    inserted.[ExternalDirectoryObjectId],
                    inserted.[CorrelationId],
                    inserted.[RequestedAtUtc],
                    inserted.[ApprovedAtUtc],
                    inserted.[CancelledAtUtc],
                    inserted.[OnboardingAttemptCount],
                    inserted.[LastFailureCode],
                    inserted.[LastFailureMessage],
                    CONVERT(NVARCHAR(260), CONVERT(VARBINARY(8), inserted.[RowVersion]), 1)
                INTO @Updated
                WHERE [AccessRequestId] = @AccessRequestId;

                SELECT * FROM @Updated;";

            return await ExecuteAdminUpdateAsync(requestId, sql, new {
                FailureCode = failureCode,
                FailureMessage = failureMessage,
                ManagedUserId = managedUserId,
                ExternalDirectoryObjectId = externalDirectoryObjectId
            });
        }

        public async Task<AccessRequestAdminRecord?> CancelAsync(string requestId, string expectedRowVersion, string reason, string? cancelledByUserId) {
            if (string.IsNullOrWhiteSpace(reason)) {
                throw new ArgumentException("Cancellation reason cannot be null or empty", nameof(reason));
            }

            return await UpdateAdminRecordAsync(
                requestId,
                expectedRowVersion,
                @"
                UPDATE [dbo].[AccessRequests]
                SET [RequestDecisionState] = N'Cancelled',
                    [OnboardingExecutionState] = N'NotRequired',
                    [CancelledByUserId] = @CancelledByUserId,
                    [CancelledAtUtc] = COALESCE([CancelledAtUtc], SYSUTCDATETIME()),
                    [CancelReason] = @CancelReason
                OUTPUT
                    CONVERT(NVARCHAR(36), inserted.[AccessRequestId]),
                    inserted.[RequestedEmail],
                    inserted.[RequestedProvider],
                    inserted.[RequestDecisionState],
                    inserted.[OnboardingExecutionState],
                    inserted.[AssignedTier],
                    inserted.[ManagedUserId],
                    inserted.[ExternalDirectoryObjectId],
                    inserted.[CorrelationId],
                    inserted.[RequestedAtUtc],
                    inserted.[ApprovedAtUtc],
                    inserted.[CancelledAtUtc],
                    inserted.[OnboardingAttemptCount],
                    inserted.[LastFailureCode],
                    inserted.[LastFailureMessage],
                    CONVERT(NVARCHAR(260), CONVERT(VARBINARY(8), inserted.[RowVersion]), 1)
                INTO @Updated
                WHERE [AccessRequestId] = @AccessRequestId
                  AND [RowVersion] = CONVERT(VARBINARY(8), @ExpectedRowVersion, 1)
                  AND (
                        [RequestDecisionState] = N'Pending'
                        OR ([RequestDecisionState] = N'Approved' AND [OnboardingExecutionState] = N'Failed')
                  );",
                new {
                    CancelledByUserId = cancelledByUserId,
                    CancelReason = reason
                });
        }

        public async Task<PublicAccessRequestResponse> CreateAsync(CreateAccessRequestRequest request, string correlationId) {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(correlationId)) {
                throw new ArgumentException("Correlation ID cannot be null or empty", nameof(correlationId));
            }

            const string sql = @"
                DECLARE @Created TABLE (
                    [RequestId] NVARCHAR(36),
                    [Email] NVARCHAR(256),
                    [Provider] NVARCHAR(50),
                    [RequestDecisionState] NVARCHAR(50),
                    [OnboardingExecutionState] NVARCHAR(50),
                    [RequestedAtUtc] DATETIME2(7),
                    [CorrelationId] NVARCHAR(128),
                    [RowVersion] NVARCHAR(260)
                );

                INSERT INTO [dbo].[AccessRequests] (
                    [RequestedEmail],
                    [RequestedProvider],
                    [CorrelationId]
                )
                OUTPUT
                    CONVERT(NVARCHAR(36), inserted.[AccessRequestId]),
                    inserted.[RequestedEmail],
                    inserted.[RequestedProvider],
                    inserted.[RequestDecisionState],
                    inserted.[OnboardingExecutionState],
                    inserted.[RequestedAtUtc],
                    inserted.[CorrelationId],
                    CONVERT(NVARCHAR(260), CONVERT(VARBINARY(8), inserted.[RowVersion]), 1)
                INTO @Created
                VALUES (
                    @Email,
                    @Provider,
                    @CorrelationId
                );

                SELECT * FROM @Created;";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var row = await connection.QuerySingleAsync(sql, new {
                    Email = request.Email,
                    Provider = request.Provider.ToString(),
                    CorrelationId = correlationId
                });

                return MapPublicResponse(row);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to create access request for {Provider}/{Email}",
                    LogSanitizer.Sanitize(request.Provider), LogSanitizer.Sanitize(request.Email));
                throw new InvalidOperationException($"Failed to create access request for {request.Email}", ex);
            }
        }

        public async Task<bool> ExistsPendingAsync(string email, IdentityProvider provider) {
            if (string.IsNullOrWhiteSpace(email)) {
                throw new ArgumentException("Email cannot be null or empty", nameof(email));
            }

            const string sql = @"
                SELECT COUNT(1)
                FROM [dbo].[AccessRequests]
                WHERE [RequestedEmail] = @Email
                  AND [RequestedProvider] = @Provider
                  AND [RequestDecisionState] = N'Pending';";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var count = await connection.ExecuteScalarAsync<int>(sql, new { Email = email, Provider = provider.ToString() });
                return count > 0;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to check pending access request for {Provider}/{Email}",
                    LogSanitizer.Sanitize(provider), LogSanitizer.Sanitize(email));
                throw new InvalidOperationException($"Failed to check access request for {email}", ex);
            }
        }

        private async Task<AccessRequestAdminRecord?> UpdateAdminRecordAsync(string requestId, string expectedRowVersion, string updateSql, object? parameters) {
            if (string.IsNullOrWhiteSpace(expectedRowVersion)) {
                throw new ArgumentException("Expected row version cannot be null or empty", nameof(expectedRowVersion));
            }

            var sql = $@"
                DECLARE @Updated TABLE (
                    [RequestId] NVARCHAR(36),
                    [Email] NVARCHAR(256),
                    [Provider] NVARCHAR(50),
                    [RequestDecisionState] NVARCHAR(50),
                    [OnboardingExecutionState] NVARCHAR(50),
                    [AssignedTier] NVARCHAR(50),
                    [ManagedUserId] NVARCHAR(128),
                    [ExternalDirectoryObjectId] NVARCHAR(128),
                    [CorrelationId] NVARCHAR(128),
                    [RequestedAtUtc] DATETIME2(7),
                    [ApprovedAtUtc] DATETIME2(7),
                    [CancelledAtUtc] DATETIME2(7),
                    [OnboardingAttemptCount] INT,
                    [LastFailureCode] NVARCHAR(100),
                    [LastFailureMessage] NVARCHAR(1000),
                    [RowVersion] NVARCHAR(260)
                );

{updateSql}

                SELECT * FROM @Updated;";

            return await ExecuteAdminUpdateAsync(requestId, sql, new {
                AccessRequestId = Guid.Parse(requestId),
                ExpectedRowVersion = expectedRowVersion,
                AssignedTier = (string?)null,
                ApprovedByUserId = (string?)null,
                CancelledByUserId = (string?)null,
                CancelReason = (string?)null,
                ManagedUserId = (string?)null,
                ExternalDirectoryObjectId = (string?)null,
                FailureCode = (string?)null,
                FailureMessage = (string?)null
            }.Merge(parameters));
        }

        private async Task<AccessRequestAdminRecord?> ExecuteAdminUpdateAsync(string requestId, string sql, object parameters) {
            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var row = await connection.QueryFirstOrDefaultAsync(sql, parameters);
                return row is null ? null : MapAdminRecord(row);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException) {
                throw new ArgumentException("Request ID must be a valid GUID", nameof(requestId), ex);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to update access request {RequestId}", LogSanitizer.Sanitize(requestId));
                throw new InvalidOperationException($"Failed to update access request {requestId}", ex);
            }
        }

        private static PublicAccessRequestResponse MapPublicResponse(dynamic row) {
            var requestDecisionState = Enum.Parse<RequestDecisionState>((string)row.RequestDecisionState, ignoreCase: true);
            var onboardingExecutionState = Enum.Parse<OnboardingExecutionState>((string)row.OnboardingExecutionState, ignoreCase: true);

            return new PublicAccessRequestResponse {
                RequestId = row.RequestId,
                Email = row.Email,
                Provider = Enum.Parse<IdentityProvider>((string)row.Provider, ignoreCase: true),
                RequesterVisibleStatus = GetRequesterVisibleStatus(requestDecisionState, onboardingExecutionState),
                StatusMessage = GetStatusMessage(requestDecisionState, onboardingExecutionState),
                NextAction = GetNextAction(requestDecisionState, onboardingExecutionState),
                RequestDecisionState = requestDecisionState,
                OnboardingExecutionState = onboardingExecutionState,
                RowState = DeriveRowState(requestDecisionState, onboardingExecutionState),
                RequestedAtUtc = row.RequestedAtUtc,
                CorrelationId = row.CorrelationId,
                RowVersion = row.RowVersion
            };
        }

        private static UserManagementRowState DeriveRowState(RequestDecisionState requestDecisionState, OnboardingExecutionState onboardingExecutionState) {
            return (requestDecisionState, onboardingExecutionState) switch {
                (RequestDecisionState.Pending, _) => UserManagementRowState.PendingApproval,
                (RequestDecisionState.Approved, OnboardingExecutionState.InProgress) => UserManagementRowState.OnboardingInProgress,
                (RequestDecisionState.Approved, OnboardingExecutionState.Failed) => UserManagementRowState.OnboardingFailed,
                (RequestDecisionState.Approved, OnboardingExecutionState.Completed) => UserManagementRowState.Active,
                _ => UserManagementRowState.Cancelled
            };
        }

        private static string GetRequesterVisibleStatus(RequestDecisionState requestDecisionState, OnboardingExecutionState onboardingExecutionState) {
            return (requestDecisionState, onboardingExecutionState) switch {
                (RequestDecisionState.Pending, _) => "PendingReview",
                (RequestDecisionState.Approved, OnboardingExecutionState.InProgress) => "ProvisioningAccess",
                (RequestDecisionState.Approved, OnboardingExecutionState.Completed) => "ApprovedReadyToSignIn",
                (RequestDecisionState.Approved, OnboardingExecutionState.Failed) => "OnboardingDelayed",
                _ => "Cancelled"
            };
        }

        private static string GetStatusMessage(RequestDecisionState requestDecisionState, OnboardingExecutionState onboardingExecutionState) {
            return (requestDecisionState, onboardingExecutionState) switch {
                (RequestDecisionState.Pending, _) => "Your access request is waiting for admin review.",
                (RequestDecisionState.Approved, OnboardingExecutionState.InProgress) => "Your request is approved and onboarding is in progress.",
                (RequestDecisionState.Approved, OnboardingExecutionState.Completed) => "Your request is approved and ready for sign-in with the selected provider.",
                (RequestDecisionState.Approved, OnboardingExecutionState.Failed) => "Your request is approved, but onboarding is delayed while an admin resolves the failure.",
                _ => "This request is cancelled. Submit a new request if you still need access."
            };
        }

        private static string GetNextAction(RequestDecisionState requestDecisionState, OnboardingExecutionState onboardingExecutionState) {
            return (requestDecisionState, onboardingExecutionState) switch {
                (RequestDecisionState.Pending, _) => "WaitForReview",
                (RequestDecisionState.Approved, OnboardingExecutionState.InProgress) => "WaitForProvisioning",
                (RequestDecisionState.Approved, OnboardingExecutionState.Completed) => "SignInWithApprovedProvider",
                (RequestDecisionState.Approved, OnboardingExecutionState.Failed) => "WaitForAdminRetry",
                _ => "SubmitNewRequest"
            };
        }

        private static AccessRequestAdminRecord MapAdminRecord(dynamic row) {
            return new AccessRequestAdminRecord {
                RequestId = row.RequestId,
                Email = row.Email,
                Provider = Enum.Parse<IdentityProvider>((string)row.Provider, ignoreCase: true),
                RequestDecisionState = Enum.Parse<RequestDecisionState>((string)row.RequestDecisionState, ignoreCase: true),
                OnboardingExecutionState = Enum.Parse<OnboardingExecutionState>((string)row.OnboardingExecutionState, ignoreCase: true),
                AssignedTier = string.IsNullOrWhiteSpace((string?)row.AssignedTier)
                    ? null
                    : Enum.Parse<TierLabel>((string)row.AssignedTier, ignoreCase: true),
                ManagedUserId = row.ManagedUserId,
                ExternalDirectoryObjectId = row.ExternalDirectoryObjectId,
                CorrelationId = row.CorrelationId,
                RequestedAtUtc = row.RequestedAtUtc,
                ApprovedAtUtc = row.ApprovedAtUtc,
                CancelledAtUtc = row.CancelledAtUtc,
                OnboardingAttemptCount = row.OnboardingAttemptCount,
                LastFailureCode = row.LastFailureCode,
                LastFailureMessage = row.LastFailureMessage,
                RowVersion = row.RowVersion
            };
        }
    }
}
