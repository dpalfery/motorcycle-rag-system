using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Provides the admin user-management read model for access requests and managed users.
/// </summary>
public class AccessRequestAdminService
{
    private readonly IAccessRequestRepository _accessRequestRepository;
    private readonly IUserManagementQueryRepository _userManagementQueryRepository;
    private readonly ApprovalOnboardingService _approvalOnboardingService;
    private readonly UserAccessLifecycleService _userAccessLifecycleService;
    private readonly ILogger<AccessRequestAdminService> _logger;

    public AccessRequestAdminService(
        IAccessRequestRepository accessRequestRepository,
        IUserManagementQueryRepository userManagementQueryRepository,
        ApprovalOnboardingService approvalOnboardingService,
        UserAccessLifecycleService userAccessLifecycleService,
        ILogger<AccessRequestAdminService> logger)
    {
        _accessRequestRepository = accessRequestRepository ?? throw new ArgumentNullException(nameof(accessRequestRepository));
        _userManagementQueryRepository = userManagementQueryRepository ?? throw new ArgumentNullException(nameof(userManagementQueryRepository));
        _approvalOnboardingService = approvalOnboardingService ?? throw new ArgumentNullException(nameof(approvalOnboardingService));
        _userAccessLifecycleService = userAccessLifecycleService ?? throw new ArgumentNullException(nameof(userAccessLifecycleService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the unified admin user-management list.
    /// </summary>
    public async Task<UserManagementListResponse> GetUserManagementRowsAsync(
        UserManagementRowState? rowState,
        string? search,
        int page,
        int pageSize)
    {
        if (page < 1)
        {
            throw new ArgumentException("Page number must be at least 1", nameof(page));
        }

        if (pageSize < 1 || pageSize > 100)
        {
            throw new ArgumentException("Page size must be between 1 and 100", nameof(pageSize));
        }

        var response = await _userManagementQueryRepository.GetRowsAsync(rowState, search, page, pageSize);
        _logger.LogInformation(
            "Retrieved {RowCount} user-management rows for page {Page} with page size {PageSize}",
            response.Rows.Length,
            page,
            pageSize);

        return response;
    }

    /// <summary>
    /// Approves a pending access request and executes approval-time onboarding.
    /// </summary>
    public async Task<AdminActionResponse> ApproveAccessRequestAsync(string requestId, ApproveAccessRequestRequest request, string? approvedByUserId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException("Request ID cannot be null or empty", nameof(requestId));
        }

        ArgumentNullException.ThrowIfNull(request);

        var current = await _accessRequestRepository.GetAdminRecordByRequestIdAsync(requestId)
            ?? throw new ArgumentException($"Access request {requestId} was not found", nameof(requestId));

        if (current.RequestDecisionState != RequestDecisionState.Pending)
        {
            throw new InvalidOperationException($"Access request {requestId} is not pending approval");
        }

        var inProgress = await _accessRequestRepository.BeginApprovalOnboardingAsync(requestId, request.Tier, request.ExpectedRowVersion, approvedByUserId)
            ?? throw new InvalidOperationException($"Access request {requestId} could not be approved. Refresh and retry.");

        await TryExecuteOnboardingAsync(inProgress);
        return await BuildActionResponseAsync(requestId);
    }

    /// <summary>
    /// Retries onboarding for an approved request that previously failed.
    /// </summary>
    public async Task<AdminActionResponse> RetryOnboardingAsync(string requestId, RetryAccessRequestOnboardingRequest request)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException("Request ID cannot be null or empty", nameof(requestId));
        }

        ArgumentNullException.ThrowIfNull(request);

        var current = await _accessRequestRepository.GetAdminRecordByRequestIdAsync(requestId)
            ?? throw new ArgumentException($"Access request {requestId} was not found", nameof(requestId));

        if (current.RequestDecisionState != RequestDecisionState.Approved || current.OnboardingExecutionState != OnboardingExecutionState.Failed)
        {
            throw new InvalidOperationException($"Access request {requestId} is not eligible for onboarding retry");
        }

        var inProgress = await _accessRequestRepository.RetryOnboardingAsync(requestId, request.ExpectedRowVersion)
            ?? throw new InvalidOperationException($"Access request {requestId} could not be retried. Refresh and retry.");

        await TryExecuteOnboardingAsync(inProgress);
        return await BuildActionResponseAsync(requestId);
    }

    /// <summary>
    /// Cancels a pending or onboarding-failed access request.
    /// </summary>
    public async Task<AdminActionResponse> CancelAccessRequestAsync(string requestId, CancelAccessRequestRequest request, string? cancelledByUserId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException("Request ID cannot be null or empty", nameof(requestId));
        }

        ArgumentNullException.ThrowIfNull(request);

        var current = await _accessRequestRepository.GetAdminRecordByRequestIdAsync(requestId)
            ?? throw new ArgumentException($"Access request {requestId} was not found", nameof(requestId));

        var canCancelPending = current.RequestDecisionState == RequestDecisionState.Pending;
        var canCancelFailedOnboarding = current.RequestDecisionState == RequestDecisionState.Approved
            && current.OnboardingExecutionState == OnboardingExecutionState.Failed;

        if (!canCancelPending && !canCancelFailedOnboarding)
        {
            throw new InvalidOperationException($"Access request {requestId} is not eligible for cancellation");
        }

        var cancelled = await _accessRequestRepository.CancelAsync(requestId, request.ExpectedRowVersion, request.Reason, cancelledByUserId)
            ?? throw new InvalidOperationException($"Access request {requestId} could not be cancelled. Refresh and retry.");

        if (!string.IsNullOrWhiteSpace(cancelled.ManagedUserId))
        {
            var userRow = await _userManagementQueryRepository.GetRowByIdAsync($"user:{cancelled.ManagedUserId}");
            if (userRow != null && userRow.ManagedUserAccessState != ManagedUserAccessState.Cancelled)
            {
                await _userAccessLifecycleService.CancelManagedUserAsync(
                    cancelled.ManagedUserId,
                    new CancelManagedUserRequest {
                        ExpectedRowVersion = userRow.RowVersion,
                        Reason = request.Reason
                    },
                    cancelledByUserId);
            }
        }

        return await BuildActionResponseAsync(requestId);
    }

    private async Task TryExecuteOnboardingAsync(AccessRequestAdminRecord record)
    {
        try
        {
            await _approvalOnboardingService.ExecuteAsync(record);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Approval-time onboarding failed for access request {RequestId}", record.RequestId);
        }
    }

    private async Task<AdminActionResponse> BuildActionResponseAsync(string requestId)
    {
        var row = await _userManagementQueryRepository.GetRowByIdAsync($"request:{requestId}")
            ?? throw new InvalidOperationException($"Updated management row for access request {requestId} was not found");

        return new AdminActionResponse { Row = row };
    }
}