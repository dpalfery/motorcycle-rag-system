using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Coordinates managed-user tier changes and access cancellation.
/// </summary>
public class UserAccessLifecycleService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserIdentityRepository _userIdentityRepository;
    private readonly IPlanRepository _planRepository;
    private readonly IUserManagementQueryRepository _userManagementQueryRepository;
    private readonly IExternalIdentityProvisioningService _externalIdentityProvisioningService;
    private readonly TierEntitlementMappingService _tierEntitlementMappingService;
    private readonly ILogger<UserAccessLifecycleService> _logger;

    public UserAccessLifecycleService(
        IUserRepository userRepository,
        IUserIdentityRepository userIdentityRepository,
        IPlanRepository planRepository,
        IUserManagementQueryRepository userManagementQueryRepository,
        IExternalIdentityProvisioningService externalIdentityProvisioningService,
        TierEntitlementMappingService tierEntitlementMappingService,
        ILogger<UserAccessLifecycleService> logger)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _userIdentityRepository = userIdentityRepository ?? throw new ArgumentNullException(nameof(userIdentityRepository));
        _planRepository = planRepository ?? throw new ArgumentNullException(nameof(planRepository));
        _userManagementQueryRepository = userManagementQueryRepository ?? throw new ArgumentNullException(nameof(userManagementQueryRepository));
        _externalIdentityProvisioningService = externalIdentityProvisioningService ?? throw new ArgumentNullException(nameof(externalIdentityProvisioningService));
        _tierEntitlementMappingService = tierEntitlementMappingService ?? throw new ArgumentNullException(nameof(tierEntitlementMappingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Changes the tier for an existing managed user and returns the latest management row.
    /// </summary>
    public async Task<AdminActionResponse> ChangeManagedUserTierAsync(string userId, ChangeManagedUserTierRequest request)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(request);

        await EnsureExpectedRowVersionAsync($"user:{userId}", request.ExpectedRowVersion, userId);

        var user = await _userRepository.GetUserByIdAsync(userId)
            ?? throw new ArgumentException($"User with ID {userId} not found", nameof(userId));

        var (planName, _) = _tierEntitlementMappingService.Resolve(request.Tier);
        var plan = await _planRepository.GetPlanByNameAsync(planName)
            ?? throw new InvalidOperationException($"Plan {planName} is not configured");

        var updated = await _userRepository.AssignTierAsync(userId, plan.Id, request.Tier);
        if (!updated)
        {
            throw new InvalidOperationException($"Failed to change tier for user {userId}");
        }

        var identityLink = await _userIdentityRepository.GetActiveByManagedUserIdAsync(userId);
        if (!string.IsNullOrWhiteSpace(identityLink?.ExternalDirectoryObjectId))
        {
            await _externalIdentityProvisioningService.ReconcileTierAssignmentsAsync(identityLink.ExternalDirectoryObjectId, request.Tier);
        }

        var row = await _userManagementQueryRepository.GetRowByIdAsync($"user:{userId}")
            ?? throw new InvalidOperationException($"Updated management row for user {userId} was not found");

        _logger.LogInformation("Changed managed user {UserId} to tier {Tier}", userId, request.Tier);
        return new AdminActionResponse { Row = row };
    }

    /// <summary>
    /// Cancels an existing managed user and returns the latest management row.
    /// </summary>
    public async Task<AdminActionResponse> CancelManagedUserAsync(string userId, CancelManagedUserRequest request, string? cancelledByUserId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(request);

        await EnsureExpectedRowVersionAsync($"user:{userId}", request.ExpectedRowVersion, userId);

        var user = await _userRepository.GetUserByIdAsync(userId)
            ?? throw new ArgumentException($"User with ID {userId} not found", nameof(userId));

        if (user.AccessState == ManagedUserAccessState.Cancelled)
        {
            throw new InvalidOperationException($"User {userId} is already cancelled");
        }

        var updated = await _userRepository.UpdateAccessStateAsync(
            userId,
            ManagedUserAccessState.Cancelled,
            isEnabled: false,
            cancelledByUserId: cancelledByUserId,
            cancelReason: request.Reason);

        if (!updated)
        {
            throw new InvalidOperationException($"Failed to cancel user {userId}");
        }

        var identityLink = await _userIdentityRepository.GetActiveByManagedUserIdAsync(userId);
        if (!string.IsNullOrWhiteSpace(identityLink?.ExternalDirectoryObjectId))
        {
            await _externalIdentityProvisioningService.RevokeAccessAsync(identityLink.ExternalDirectoryObjectId);
            await _userIdentityRepository.MarkAccessRevokedAsync(userId);
        }

        var row = await _userManagementQueryRepository.GetRowByIdAsync($"user:{userId}")
            ?? throw new InvalidOperationException($"Updated management row for user {userId} was not found");

        _logger.LogInformation("Cancelled managed user {UserId}", userId);
        return new AdminActionResponse { Row = row };
    }

    private async Task EnsureExpectedRowVersionAsync(string rowId, string expectedRowVersion, string userId)
    {
        if (string.IsNullOrWhiteSpace(expectedRowVersion))
        {
            throw new ArgumentException("Expected row version is required", nameof(expectedRowVersion));
        }

        var currentRow = await _userManagementQueryRepository.GetRowByIdAsync(rowId)
            ?? throw new InvalidOperationException($"Management row for user {userId} was not found");

        if (!string.Equals(currentRow.RowVersion, expectedRowVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"User {userId} has changed since it was loaded. Refresh and retry.");
        }
    }
}