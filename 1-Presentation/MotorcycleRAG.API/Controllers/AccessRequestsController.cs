using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Models.DTOs;
using System.Net.Mime;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Handles anonymous onboarding access-request submission from the login page.
/// </summary>
[ApiController]
[Route("api/access-requests")]
[AllowAnonymous]
[EnableRateLimiting("public")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
public sealed class AccessRequestsController : ControllerBase
{
    private readonly AccessRequestService _accessRequestService;
    private readonly ILogger<AccessRequestsController> _logger;

    public AccessRequestsController(AccessRequestService accessRequestService, ILogger<AccessRequestsController> logger)
    {
        _accessRequestService = accessRequestService ?? throw new ArgumentNullException(nameof(accessRequestService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Submits a new access request or returns the current state of an existing request.
    /// </summary>
    [HttpPost]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(PublicAccessRequestResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(PublicAccessRequestResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateAccessRequestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var (response, created) = await _accessRequestService.CreateOrGetExistingAsync(request);
            return created ? Accepted(response) : Conflict(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid access request payload received");
            return BadRequest(new { error = ex.Message, code = "InvalidPayload" });
        }
    }
}