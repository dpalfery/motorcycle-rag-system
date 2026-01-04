using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

using System.Net.Mime;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Provides RESTful endpoints for the current authenticated user's profile and usage information.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public sealed class MeController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUserRepository _userRepository;
    private readonly IUsageTrackingService _usageTrackingService;
    private readonly IPlanPolicyService _planPolicyService;
    private readonly ILogger<MeController> _logger;

    public MeController(
        ICurrentUserService currentUserService,
        IUserRepository userRepository,
        IUsageTrackingService usageTrackingService,
        IPlanPolicyService planPolicyService,
        ILogger<MeController> logger)
    {
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _usageTrackingService = usageTrackingService ?? throw new ArgumentNullException(nameof(usageTrackingService));
        _planPolicyService = planPolicyService ?? throw new ArgumentNullException(nameof(planPolicyService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the current authenticated user's profile information.
    /// </summary>
    /// <returns>User profile information</returns>
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfileAsync()
    {
        var userId = _currentUserService.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("Profile request without authenticated user");
            return Unauthorized(new { error = "Authentication required" });
        }

        var user = await _userRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found in database", userId);
            return NotFound(new { error = "User not found" });
        }

        var dailyLimit = await _planPolicyService.GetDailyRequestLimitAsync(user);
        var remaining = await _planPolicyService.GetRemainingDailyRequestsAsync(userId);

        var response = new UserProfileResponse
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

        _logger.LogInformation("Retrieved profile for user {UserId}", userId);
        return Ok(response);
    }

    /// <summary>
    /// Gets the current authenticated user's usage information.
    /// </summary>
    /// <param name="days">Number of days to retrieve usage for (default: 7, max: 30)</param>
    /// <returns>User usage information</returns>
    [HttpGet("usage")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UsageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUsageAsync([FromQuery] int days = 7)
    {
        var userId = _currentUserService.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("Usage request without authenticated user");
            return Unauthorized(new { error = "Authentication required" });
        }

        // Validate and clamp days parameter
        if (days < 1)
        {
            days = 1;
        }
        else if (days > 30)
        {
            days = 30;
        }

        var startDate = DateTime.UtcNow.AddDays(-days);
        var endDate = DateTime.UtcNow;

        var usageRecords = await _usageTrackingService.GetUsageByDateRangeAsync(userId, startDate, endDate);
        var dailyCount = await _planPolicyService.GetDailyUsageCountAsync(userId);
        
        // Get user to determine daily limit
        var user = await _userRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found when getting usage", userId);
            return NotFound(new { error = "User not found" });
        }
        var dailyLimit = await _planPolicyService.GetDailyRequestLimitAsync(user);

        var response = new UsageResponse
        {
            UserId = userId,
            StartDate = startDate,
            EndDate = endDate,
            TotalRequests = usageRecords.Length,
            SuccessfulRequests = usageRecords.Count(u => u.IsSuccess),
            FailedRequests = usageRecords.Count(u => !u.IsSuccess),
            DailyUsageCount = dailyCount,
            DailyRequestLimit = dailyLimit,
            RemainingDailyRequests = Math.Max(0, dailyLimit - dailyCount),
            UsageRecords = usageRecords.Select(u => new UsageRecord
            {
                Id = u.Id,
                Endpoint = u.Endpoint,
                HttpMethod = u.HttpMethod,
                QueryId = u.QueryId,
                RequestTime = u.RequestTime,
                DurationMs = u.DurationMs,
                StatusCode = u.StatusCode,
                IsSuccess = u.IsSuccess
            }).ToArray()
        };

        _logger.LogInformation("Retrieved usage for user {UserId} over {Days} days", userId, days);
        return Ok(response);
    }

    /// <summary>
    /// Updates the current authenticated user's profile information.
    /// Only allowed fields: DisplayName, FirstName, LastName
    /// </summary>
    /// <param name="request">Profile update request</param>
    /// <returns>Updated user profile</returns>
    [HttpPatch]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfileAsync([FromBody] UpdateProfileRequest request)
    {
        var userId = _currentUserService.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("Profile update request without authenticated user");
            return Unauthorized(new { error = "Authentication required" });
        }

        var user = await _userRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found in database", userId);
            return NotFound(new { error = "User not found" });
        }

        // Validate request
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.DisplayName) && 
            string.IsNullOrWhiteSpace(request.FirstName) && 
            string.IsNullOrWhiteSpace(request.LastName))
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

        if (errors.Count > 0)
        {
            return BadRequest(new { errors });
        }

        // Update allowed fields only
        var needsUpdate = false;

        if (!string.IsNullOrWhiteSpace(request.DisplayName) && user.DisplayName != request.DisplayName)
        {
            user.DisplayName = request.DisplayName;
            needsUpdate = true;
        }

        if (!string.IsNullOrWhiteSpace(request.FirstName) && user.FirstName != request.FirstName)
        {
            user.FirstName = request.FirstName;
            needsUpdate = true;
        }

        if (!string.IsNullOrWhiteSpace(request.LastName) && user.LastName != request.LastName)
        {
            user.LastName = request.LastName;
            needsUpdate = true;
        }

        if (needsUpdate)
        {
            user.LastUpdatedDate = DateTime.UtcNow;
            await _userRepository.UpdateUserAsync(user);
            _logger.LogInformation("Updated profile for user {UserId}", userId);
        }

        var dailyLimit = await _planPolicyService.GetDailyRequestLimitAsync(user);
        var remaining = await _planPolicyService.GetRemainingDailyRequestsAsync(userId);

        var response = new UserProfileResponse
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

        return Ok(response);
    }
}