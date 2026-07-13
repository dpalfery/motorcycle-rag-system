using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Provides RESTful endpoints for the current authenticated user's profile and usage information.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
public sealed class MeController : ControllerBase
{
    private readonly ICurrentUserProfileService _currentUserProfileService;

    public MeController(ICurrentUserProfileService currentUserProfileService)
    {
        _currentUserProfileService = currentUserProfileService ?? throw new ArgumentNullException(nameof(currentUserProfileService));
    }

    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetProfileAsync() =>
        MapProfileResult(await _currentUserProfileService.GetProfileAsync(RequestCancellationToken).ConfigureAwait(false));

    [HttpGet("usage")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UsageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsageAsync() =>
        MapUsageResult(await _currentUserProfileService.GetUsageAsync(7, RequestCancellationToken).ConfigureAwait(false));

    [HttpGet("usage-by-days")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UsageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsageAsync([FromQuery] int days) =>
        MapUsageResult(await _currentUserProfileService.GetUsageAsync(days, RequestCancellationToken).ConfigureAwait(false));

    [HttpPatch]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateProfileAsync([FromBody] UpdateProfileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MapProfileResult(await _currentUserProfileService.UpdateProfileAsync(request, RequestCancellationToken).ConfigureAwait(false));
    }

    private IActionResult MapProfileResult(CurrentUserProfileResult result) => result.Status switch
    {
        CurrentUserProfileStatus.Success => Ok(result.Profile),
        CurrentUserProfileStatus.Unauthenticated => Unauthorized(new { error = "Authentication required" }),
        CurrentUserProfileStatus.AccessNotApproved => StatusCode(StatusCodes.Status403Forbidden, new { error = "Access has not been approved for this account" }),
        CurrentUserProfileStatus.ValidationFailed => BadRequest(new { errors = result.ValidationErrors }),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };

    private IActionResult MapUsageResult(CurrentUserUsageResult result) => result.Status switch
    {
        CurrentUserProfileStatus.Success => Ok(result.Usage),
        CurrentUserProfileStatus.Unauthenticated => Unauthorized(new { error = "Authentication required" }),
        CurrentUserProfileStatus.AccessNotApproved => StatusCode(StatusCodes.Status403Forbidden, new { error = "Access has not been approved for this account" }),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };

    private CancellationToken RequestCancellationToken => ControllerContext.HttpContext?.RequestAborted ?? CancellationToken.None;
}
