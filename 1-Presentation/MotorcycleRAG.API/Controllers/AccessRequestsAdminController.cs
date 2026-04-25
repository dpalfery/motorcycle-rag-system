using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using System.Net.Mime;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Admin endpoints for the unified onboarding and managed-user review surface.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = AuthorizationPolicyNames.Admin)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
public sealed class AccessRequestsAdminController : ControllerBase {
    private readonly AccessRequestAdminService _accessRequestAdminService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AccessRequestsAdminController> _logger;

    public AccessRequestsAdminController(
        AccessRequestAdminService accessRequestAdminService,
        ICurrentUserService currentUserService,
        ILogger<AccessRequestsAdminController> logger) {
        _accessRequestAdminService = accessRequestAdminService ?? throw new ArgumentNullException(nameof(accessRequestAdminService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lists unified management rows for pending access requests and existing managed users.
    /// </summary>
    [HttpGet("user-management")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UserManagementListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUserManagementAsync(
        [FromQuery] UserManagementRowState? rowState,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50) {
        try {
            var response = await _accessRequestAdminService.GetUserManagementRowsAsync(rowState, search, page, pageSize);
            return Ok(response);
        }
        catch (ArgumentException ex) {
            _logger.LogWarning(ex, "Invalid user-management list request");
            return BadRequest(new { error = ex.Message, code = "InvalidPayload" });
        }
    }

    /// <summary>
    /// Approves a pending access request and starts approval-time onboarding.
    /// </summary>
    [HttpPost("access-requests/{requestId}/approve")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(AdminActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveAccessRequestAsync(string requestId, [FromBody] ApproveAccessRequestRequest request) {
        if (string.IsNullOrWhiteSpace(requestId)) {
            return BadRequest(new { error = "Request ID is required" });
        }

        if (request == null) {
            return BadRequest(new { error = "Request body is required" });
        }

        try {
            var approvedByUserId = await _currentUserService.GetManagedUserIdAsync();
            var response = await _accessRequestAdminService.ApproveAccessRequestAsync(requestId, request, approvedByUserId);
            return Ok(response);
        }
        catch (ArgumentException ex) {
            _logger.LogWarning(ex, "Invalid approval request for access request {RequestId}", requestId);
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) {
            _logger.LogWarning(ex, "Approval could not be completed for access request {RequestId}", requestId);
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Retries approval-time onboarding for a previously failed request.
    /// </summary>
    [HttpPost("access-requests/{requestId}/retry-onboarding")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(AdminActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetryOnboardingAsync(string requestId, [FromBody] RetryAccessRequestOnboardingRequest request) {
        if (string.IsNullOrWhiteSpace(requestId)) {
            return BadRequest(new { error = "Request ID is required" });
        }

        if (request == null) {
            return BadRequest(new { error = "Request body is required" });
        }

        try {
            var response = await _accessRequestAdminService.RetryOnboardingAsync(requestId, request);
            return Ok(response);
        }
        catch (ArgumentException ex) {
            _logger.LogWarning(ex, "Invalid retry request for access request {RequestId}", requestId);
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) {
            _logger.LogWarning(ex, "Retry could not be completed for access request {RequestId}", requestId);
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Cancels a pending or onboarding-failed access request.
    /// </summary>
    [HttpPost("access-requests/{requestId}/cancel")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(AdminActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelAccessRequestAsync(string requestId, [FromBody] CancelAccessRequestRequest request) {
        if (string.IsNullOrWhiteSpace(requestId)) {
            return BadRequest(new { error = "Request ID is required" });
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Reason)) {
            return BadRequest(new { error = "Cancellation reason is required" });
        }

        try {
            var cancelledByUserId = await _currentUserService.GetManagedUserIdAsync();
            var response = await _accessRequestAdminService.CancelAccessRequestAsync(requestId, request, cancelledByUserId);
            return Ok(response);
        }
        catch (ArgumentException ex) {
            _logger.LogWarning(ex, "Invalid cancellation request for access request {RequestId}", requestId);
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) {
            _logger.LogWarning(ex, "Cancellation could not be completed for access request {RequestId}", requestId);
            return Conflict(new { error = ex.Message });
        }
    }
}
