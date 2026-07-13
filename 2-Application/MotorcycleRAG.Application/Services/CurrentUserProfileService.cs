using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Coordinates current-user profile and usage operations without exposing persistence to HTTP adapters.
/// </summary>
public sealed class CurrentUserProfileService : ICurrentUserProfileService
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUserRepository _userRepository;
    private readonly IUsageTrackingService _usageTrackingService;
    private readonly IPlanPolicyService _planPolicyService;
    private readonly ILogger<CurrentUserProfileService> _logger;

    public CurrentUserProfileService(
        ICurrentUserService currentUserService,
        IUserRepository userRepository,
        IUsageTrackingService usageTrackingService,
        IPlanPolicyService planPolicyService,
        ILogger<CurrentUserProfileService> logger)
    {
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _usageTrackingService = usageTrackingService ?? throw new ArgumentNullException(nameof(usageTrackingService));
        _planPolicyService = planPolicyService ?? throw new ArgumentNullException(nameof(planPolicyService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CurrentUserProfileResult> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var user = await GetApprovedUserAsync().ConfigureAwait(false);
        if (user.Status != CurrentUserProfileStatus.Success)
        {
            return new(user.Status);
        }

        return new(CurrentUserProfileStatus.Success, await BuildProfileAsync(user.User!).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<CurrentUserUsageResult> GetUsageAsync(int days, CancellationToken cancellationToken = default)
    {
        var user = await GetApprovedUserAsync().ConfigureAwait(false);
        if (user.Status != CurrentUserProfileStatus.Success)
        {
            return new(user.Status);
        }

        days = Math.Clamp(days, 1, 30);
        var endDate = DateTime.UtcNow;
        var startDate = endDate.AddDays(-days);
        var usageRecords = await _usageTrackingService.GetUsageByDateRangeAsync(user.User!.Id, startDate, endDate).ConfigureAwait(false);
        var dailyCount = await _planPolicyService.GetDailyUsageCountAsync(user.User.Id).ConfigureAwait(false);
        var dailyLimit = await _planPolicyService.GetDailyRequestLimitAsync(user.User).ConfigureAwait(false);

        _logger.LogInformation("Retrieved usage for user {UserId} over {Days} days", user.User.Id, days);
        return new(CurrentUserProfileStatus.Success, new UsageResponse
        {
            UserId = user.User.Id,
            StartDate = startDate,
            EndDate = endDate,
            TotalRequests = usageRecords.Length,
            SuccessfulRequests = usageRecords.Count(static usage => usage.IsSuccess),
            FailedRequests = usageRecords.Count(static usage => !usage.IsSuccess),
            DailyUsageCount = dailyCount,
            DailyRequestLimit = dailyLimit,
            RemainingDailyRequests = Math.Max(0, dailyLimit - dailyCount),
            UsageRecords = usageRecords.Select(usage => new UsageRecord
            {
                Id = usage.Id,
                Endpoint = usage.Endpoint,
                HttpMethod = usage.HttpMethod,
                QueryId = usage.QueryId,
                RequestTime = usage.RequestTime,
                DurationMs = usage.DurationMs,
                StatusCode = usage.StatusCode,
                IsSuccess = usage.IsSuccess
            }).ToArray()
        });
    }

    /// <inheritdoc />
    public async Task<CurrentUserProfileResult> UpdateProfileAsync(
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await GetApprovedUserAsync().ConfigureAwait(false);
        if (user.Status != CurrentUserProfileStatus.Success)
        {
            return new(user.Status);
        }

        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return new(CurrentUserProfileStatus.ValidationFailed, ValidationErrors: validationErrors);
        }

        var managedUser = user.User!;
        var needsUpdate = false;
        if (!string.IsNullOrWhiteSpace(request.DisplayName) && managedUser.DisplayName != request.DisplayName)
        {
            managedUser.DisplayName = request.DisplayName;
            needsUpdate = true;
        }

        if (!string.IsNullOrWhiteSpace(request.FirstName) && managedUser.FirstName != request.FirstName)
        {
            managedUser.FirstName = request.FirstName;
            needsUpdate = true;
        }

        if (!string.IsNullOrWhiteSpace(request.LastName) && managedUser.LastName != request.LastName)
        {
            managedUser.LastName = request.LastName;
            needsUpdate = true;
        }

        if (needsUpdate)
        {
            managedUser.LastUpdatedDate = DateTime.UtcNow;
            await _userRepository.UpdateUserAsync(managedUser).ConfigureAwait(false);
            _logger.LogInformation("Updated profile for user {UserId}", managedUser.Id);
        }

        return new(CurrentUserProfileStatus.Success, await BuildProfileAsync(managedUser).ConfigureAwait(false));
    }

    private async Task<(CurrentUserProfileStatus Status, UserDTO? User)> GetApprovedUserAsync()
    {
        if (!_currentUserService.IsAuthenticated)
        {
            _logger.LogWarning("Current-user use case invoked without an authenticated user");
            return (CurrentUserProfileStatus.Unauthenticated, null);
        }

        var user = await _currentUserService.GetManagedUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            _logger.LogWarning("Authenticated principal does not resolve to an approved managed user");
            return (CurrentUserProfileStatus.AccessNotApproved, null);
        }

        return (CurrentUserProfileStatus.Success, user);
    }

    private async Task<UserProfileResponse> BuildProfileAsync(UserDTO user)
    {
        var dailyLimit = await _planPolicyService.GetDailyRequestLimitAsync(user).ConfigureAwait(false);
        var remaining = await _planPolicyService.GetRemainingDailyRequestsAsync(user.Id).ConfigureAwait(false);
        _logger.LogInformation("Retrieved profile for user {UserId}", user.Id);
        return new UserProfileResponse
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            FirstName = user.FirstName,
            LastName = user.LastName,
            IsEnabled = user.IsEnabled,
            CreatedDate = user.CreatedDate,
            LastUpdatedDate = user.LastUpdatedDate,
            PlanId = user.PlanId,
            DailyRequestLimit = dailyLimit,
            RemainingDailyRequests = remaining
        };
    }

    private static List<string> Validate(UpdateProfileRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.DisplayName) && string.IsNullOrWhiteSpace(request.FirstName) && string.IsNullOrWhiteSpace(request.LastName))
        {
            errors.Add("At least one field (DisplayName, FirstName, LastName) must be provided");
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName) && request.DisplayName.Length > 100)
        {
            errors.Add("DisplayName cannot exceed 100 characters");
        }

        if (!string.IsNullOrWhiteSpace(request.FirstName) && request.FirstName.Length > 50)
        {
            errors.Add("FirstName cannot exceed 50 characters");
        }

        if (!string.IsNullOrWhiteSpace(request.LastName) && request.LastName.Length > 50)
        {
            errors.Add("LastName cannot exceed 50 characters");
        }

        return errors;
    }
}
