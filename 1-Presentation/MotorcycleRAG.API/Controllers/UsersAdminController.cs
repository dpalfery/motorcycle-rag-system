using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;
using System.Text.Json.Serialization;

using System.Net.Mime;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Provides RESTful endpoints for administrative user management.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "Admin")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
public sealed class UsersAdminController : ControllerBase {
    private readonly IUserAdminService _userAdminService;
    private readonly ILogger<UsersAdminController> _logger;

    public UsersAdminController(
        IUserAdminService userAdminService,
        ILogger<UsersAdminController> logger) {
        _userAdminService = userAdminService ?? throw new ArgumentNullException(nameof(userAdminService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Enables or disables a user account.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="request">Enable/disable request</param>
    /// <returns>Updated user information</returns>
    [HttpPut("{userId}/enabled")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(MotorcycleRAG.Contracts.Models.DTOs.UserDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetUserEnabledStatusAsync(
        string userId,
        [FromBody] SetUserEnabledRequest request) {
        if (string.IsNullOrWhiteSpace(userId)) {
            return BadRequest(new { error = "User ID is required" });
        }

        if (request == null) {
            return BadRequest(new { error = "Request body is required" });
        }

        try {
            var user = await _userAdminService.SetUserEnabledStatusAsync(userId, request.IsEnabled);
            _logger.LogInformation("Admin set user {UserId} enabled status to {IsEnabled}", userId, request.IsEnabled);
            return Ok(user);
        }
        catch (ArgumentException ex) {
            _logger.LogWarning(ex, "Invalid request to set user enabled status for {UserId}", userId);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error setting user enabled status for {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Assigns a plan to a user.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="request">Plan assignment request</param>
    /// <returns>Updated user information</returns>
    [HttpPut("{userId}/plan")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(MotorcycleRAG.Contracts.Models.DTOs.UserDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignPlanToUserAsync(
        string userId,
        [FromBody] AssignPlanRequest request) {
        if (string.IsNullOrWhiteSpace(userId)) {
            return BadRequest(new { error = "User ID is required" });
        }

        if (request == null || string.IsNullOrWhiteSpace(request.PlanId)) {
            return BadRequest(new { error = "Plan ID is required" });
        }

        try {
            var user = await _userAdminService.AssignPlanToUserAsync(userId, request.PlanId);
            _logger.LogInformation("Admin assigned plan {PlanId} to user {UserId}", request.PlanId, userId);
            return Ok(user);
        }
        catch (ArgumentException ex) {
            _logger.LogWarning(ex, "Invalid request to assign plan to user {UserId}", userId);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error assigning plan to user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Gets all users (admin view) with custom pagination.
    /// </summary>
    /// <param name="page">Page number (minimum 1)</param>
    /// <param name="pageSize">Page size (1-100)</param>
    /// <returns>Paged list of users</returns>
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UserListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllUsersAsync([FromQuery] int page, [FromQuery] int pageSize) {
        // Validate pagination parameters
        if (page < 1) {
            return BadRequest(new { error = "Page must be greater than or equal to 1" });
        }

        const int maxPageSize = 100;
        if (pageSize < 1 || pageSize > maxPageSize) {
            return BadRequest(new { error = $"PageSize must be between 1 and {maxPageSize}" });
        }

        return await GetAllUsersInternalAsync(page, pageSize);
    }

    /// <summary>
    /// Internal implementation for getting all users.
    /// </summary>
    private async Task<IActionResult> GetAllUsersInternalAsync(int page, int pageSize) {
        try {
            var users = await _userAdminService.GetAllUsersAsync(page, pageSize);
            _logger.LogInformation("Admin retrieved {Count} users (page: {Page}, pageSize: {PageSize})", users.Length, page, pageSize);

            var response = new UserListResponse {
                Users = users,
                Page = page,
                PageSize = pageSize,
                TotalCount = users.Length
            };

            return Ok(response);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving users");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }
}

/// <summary>
/// Set user enabled status request model
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "DTOs must be public for API documentation")]
public class SetUserEnabledRequest {
    [JsonRequired]
    public bool IsEnabled { get; set; }
}

/// <summary>
/// Assign plan request model
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "DTOs must be public for API documentation")]
public class AssignPlanRequest {
    public string PlanId { get; set; } = string.Empty;
}

/// <summary>
/// User list response model
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "DTOs must be public for API documentation")]
public class UserListResponse {
    public IReadOnlyList<MotorcycleRAG.Contracts.Models.DTOs.UserDTO> Users { get; set; } = Array.Empty<MotorcycleRAG.Contracts.Models.DTOs.UserDTO>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}