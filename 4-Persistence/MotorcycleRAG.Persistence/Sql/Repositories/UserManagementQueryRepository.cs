using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Utilities;


namespace MotorcycleRAG.Persistence.Sql.Repositories {
    /// <summary>
    /// ADO.NET implementation of the unified admin user-management projection.
    /// </summary>
    public class UserManagementQueryRepository : IUserManagementQueryRepository {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<UserManagementQueryRepository> _logger;

        public UserManagementQueryRepository(ISqlConnectionFactory connectionFactory, ILogger<UserManagementQueryRepository> logger) {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<UserManagementListResponse> GetRowsAsync(UserManagementRowState? rowState, string? search, int page, int pageSize) {
            if (page < 1) {
                throw new ArgumentException("Page number must be at least 1", nameof(page));
            }

            if (pageSize < 1) {
                throw new ArgumentException("Page size must be at least 1", nameof(pageSize));
            }

            var filter = BuildFilter(rowState, search);
            var offset = (page - 1) * pageSize;

            var sql = $@"
                WITH ManagementRows AS (
                    {BaseQuery()}
                )
                SELECT *
                FROM ManagementRows
                {filter.WhereClause}
                ORDER BY COALESCE([RequestedAtUtc], [ApprovedAtUtc], [CancelledAtUtc]) DESC, [RowId] ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

                WITH ManagementRows AS (
                    {BaseQuery()}
                )
                SELECT COUNT(1)
                FROM ManagementRows
                {filter.WhereClause};";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                using var multi = await connection.QueryMultipleAsync(sql, new {
                    RowState = rowState?.ToString(),
                    Search = filter.Search,
                    Offset = offset,
                    PageSize = pageSize
                });

                var rows = (await multi.ReadAsync()).Select(MapRow).ToArray();
                var totalCount = await multi.ReadSingleAsync<int>();

                return new UserManagementListResponse {
                    Rows = rows,
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount
                };
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to query user-management rows");
                throw new InvalidOperationException("Failed to query user-management rows", ex);
            }
        }

        public async Task<UserManagementRow?> GetRowByIdAsync(string rowId) {
            if (string.IsNullOrWhiteSpace(rowId)) {
                throw new ArgumentException("Row ID cannot be null or empty", nameof(rowId));
            }

            string? requestId = null;
            string? managedUserId = null;

            if (rowId.StartsWith("request:", StringComparison.OrdinalIgnoreCase)) {
                requestId = rowId.Substring("request:".Length);
            }
            else if (rowId.StartsWith("user:", StringComparison.OrdinalIgnoreCase)) {
                managedUserId = rowId.Substring("user:".Length);
            }
            else {
                requestId = rowId;
                managedUserId = rowId;
            }

            var sql = $@"
                WITH ManagementRows AS (
                    {BaseQuery()}
                )
                SELECT *
                FROM ManagementRows
                WHERE [RowId] = @ExactRowId
                   OR [AccessRequestId] = @RequestId
                   OR [ManagedUserId] = @ManagedUserId;";

            try {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                var row = await connection.QueryFirstOrDefaultAsync(sql, new {
                    ExactRowId = rowId,
                    RequestId = requestId,
                    ManagedUserId = managedUserId
                });

                return row is null ? null : MapRow(row);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to query management row {RowId}", LogSanitizer.Sanitize(rowId));
                throw new InvalidOperationException($"Failed to query management row {rowId}", ex);
            }
        }

        private static (string WhereClause, string? Search) BuildFilter(UserManagementRowState? rowState, string? search) {
            var clauses = new StringBuilder();
            string? normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";

            if (rowState.HasValue || normalizedSearch != null) {
                clauses.Append("WHERE 1 = 1");

                if (rowState.HasValue) {
                    clauses.Append(" AND [RowState] = @RowState");
                }

                if (normalizedSearch != null) {
                    clauses.Append(" AND ([Email] LIKE @Search OR [Provider] LIKE @Search)");
                }
            }

            return (clauses.ToString(), normalizedSearch);
        }

        private static string BaseQuery() {
            return @"
                SELECT
                    CONCAT(N'request:', CONVERT(NVARCHAR(36), ar.[AccessRequestId])) AS [RowId],
                    N'PendingRequest' AS [RowType],
                    CONVERT(NVARCHAR(36), ar.[AccessRequestId]) AS [AccessRequestId],
                    ar.[ManagedUserId] AS [ManagedUserId],
                    ar.[RequestedEmail] AS [Email],
                    ar.[RequestedProvider] AS [Provider],
                    ar.[AssignedTier] AS [AssignedTier],
                    ar.[RequestDecisionState] AS [RequestDecisionState],
                    ar.[OnboardingExecutionState] AS [OnboardingExecutionState],
                    CASE WHEN ar.[ManagedUserId] IS NULL THEN N'None' ELSE COALESCE(u.[AccessState], N'None') END AS [ManagedUserAccessState],
                    CASE
                        WHEN ar.[RequestDecisionState] = N'Pending' THEN N'PendingApproval'
                        WHEN ar.[RequestDecisionState] = N'Approved' AND ar.[OnboardingExecutionState] = N'InProgress' THEN N'OnboardingInProgress'
                        WHEN ar.[RequestDecisionState] = N'Approved' AND ar.[OnboardingExecutionState] = N'Failed' THEN N'OnboardingFailed'
                        WHEN ar.[RequestDecisionState] = N'Approved' AND ar.[OnboardingExecutionState] = N'Completed' THEN N'Active'
                        ELSE N'Cancelled'
                    END AS [RowState],
                    CASE
                        WHEN ar.[RequestDecisionState] = N'Pending' THEN N'Approve,Cancel'
                        WHEN ar.[RequestDecisionState] = N'Approved' AND ar.[OnboardingExecutionState] = N'Failed' THEN N'RetryOnboarding,Cancel'
                        ELSE N''
                    END AS [AllowedActions],
                    ar.[CorrelationId] AS [CorrelationId],
                    ar.[RequestedAtUtc] AS [RequestedAtUtc],
                    ar.[ApprovedAtUtc] AS [ApprovedAtUtc],
                    ar.[CancelledAtUtc] AS [CancelledAtUtc],
                    ar.[LastFailureCode] AS [LastFailureCode],
                    ar.[LastFailureMessage] AS [LastFailureMessage],
                    sys.fn_varbintohexstr(ar.[RowVersion]) AS [RowVersion]
                FROM [dbo].[AccessRequests] ar
                LEFT JOIN [dbo].[Users] u ON u.[Id] = ar.[ManagedUserId]

                UNION ALL

                SELECT
                    CONCAT(N'user:', u.[Id]) AS [RowId],
                    N'ManagedUser' AS [RowType],
                    NULL AS [AccessRequestId],
                    u.[Id] AS [ManagedUserId],
                    u.[Email] AS [Email],
                    COALESCE(ui.[Provider], u.[AuthProvider], N'Microsoft') AS [Provider],
                    u.[TierLabel] AS [AssignedTier],
                    N'Approved' AS [RequestDecisionState],
                    CASE WHEN u.[AccessState] = N'Active' THEN N'Completed' ELSE N'NotRequired' END AS [OnboardingExecutionState],
                    COALESCE(u.[AccessState], N'None') AS [ManagedUserAccessState],
                    CASE WHEN u.[AccessState] = N'Active' THEN N'Active' ELSE N'Cancelled' END AS [RowState],
                    CASE WHEN u.[AccessState] = N'Active' THEN N'ChangeTier,Cancel' ELSE N'' END AS [AllowedActions],
                    NULL AS [CorrelationId],
                    u.[CreatedDate] AS [RequestedAtUtc],
                    u.[LastUpdatedDate] AS [ApprovedAtUtc],
                    u.[CancelledAtUtc] AS [CancelledAtUtc],
                    NULL AS [LastFailureCode],
                    NULL AS [LastFailureMessage],
                    sys.fn_varbintohexstr(u.[RowVersion]) AS [RowVersion]
                FROM [dbo].[Users] u
                OUTER APPLY (
                    SELECT TOP (1) [Provider]
                    FROM [dbo].[UserIdentities]
                    WHERE [ManagedUserId] = u.[Id]
                    ORDER BY [LastSyncedAtUtc] DESC, [InvitationCreatedAtUtc] DESC
                ) ui";
        }

        private static UserManagementRow MapRow(dynamic row) {
            return new UserManagementRow {
                RowId = row.RowId,
                RowType = row.RowType,
                AccessRequestId = row.AccessRequestId,
                ManagedUserId = row.ManagedUserId,
                Email = row.Email,
                Provider = Enum.Parse<IdentityProvider>((string)row.Provider, ignoreCase: true),
                AssignedTier = string.IsNullOrWhiteSpace((string?)row.AssignedTier)
                    ? null
                    : Enum.Parse<TierLabel>((string)row.AssignedTier, ignoreCase: true),
                RequestDecisionState = Enum.Parse<RequestDecisionState>((string)row.RequestDecisionState, ignoreCase: true),
                OnboardingExecutionState = Enum.Parse<OnboardingExecutionState>((string)row.OnboardingExecutionState, ignoreCase: true),
                ManagedUserAccessState = Enum.Parse<ManagedUserAccessState>((string)row.ManagedUserAccessState, ignoreCase: true),
                RowState = Enum.Parse<UserManagementRowState>((string)row.RowState, ignoreCase: true),
                AllowedActions = SplitActions((string?)row.AllowedActions),
                CorrelationId = row.CorrelationId ?? string.Empty,
                RequestedAtUtc = row.RequestedAtUtc,
                ApprovedAtUtc = row.ApprovedAtUtc,
                CancelledAtUtc = row.CancelledAtUtc,
                LastFailureCode = row.LastFailureCode,
                LastFailureMessage = row.LastFailureMessage,
                RowVersion = row.RowVersion
            };
        }

        private static string[] SplitActions(string? actions) {
            return string.IsNullOrWhiteSpace(actions)
                ? Array.Empty<string>()
                : actions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
