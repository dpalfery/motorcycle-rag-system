using System.Net.Mime;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Provides RESTful endpoints for administrative plan management.
/// </summary>
[ApiController]
[Route("api/admin/plans")]
[Authorize(Policy = "mcr-api-admin")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
public sealed class PlansAdminController : ControllerBase
{
    private readonly IPlanAdministrationService _planAdministrationService;
    private readonly ILogger<PlansAdminController> _logger;

    public PlansAdminController(IPlanAdministrationService planAdministrationService, ILogger<PlansAdminController> logger)
    {
        _planAdministrationService = planAdministrationService ?? throw new ArgumentNullException(nameof(planAdministrationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UserPlan[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllPlansAsync()
    {
        try
        {
            return Ok(await _planAdministrationService.GetAllPlansAsync(RequestCancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error retrieving plans");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    [HttpGet("{planId}")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UserPlan), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanByIdAsync(string planId)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            return BadRequest(new { error = "Plan ID is required" });
        }

        try
        {
            var plan = await _planAdministrationService.GetPlanByIdAsync(planId, RequestCancellationToken).ConfigureAwait(false);
            return plan is null ? NotFound(new { error = "Plan not found" }) : Ok(plan);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error retrieving plan");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    [HttpPost]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UserPlan), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreatePlanAsync([FromBody] CreatePlanRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required" });
        }

        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return BadRequest(new { errors = validationErrors });
        }

        try
        {
            var createdPlan = await _planAdministrationService.CreatePlanAsync(
                new PlanCreateCommand(request!.Name, request.Description ?? string.Empty, request.DailyRequestLimit, request.IsPaid),
                RequestCancellationToken).ConfigureAwait(false);
            return Created(new Uri($"/api/admin/plans/{createdPlan.Id}", UriKind.Relative), createdPlan);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error creating plan");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    [HttpPut("{planId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(UserPlan), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePlanAsync(string planId, [FromBody] UpdatePlanRequest? request)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            return BadRequest(new { error = "Plan ID is required" });
        }

        if (request is null)
        {
            return BadRequest(new { error = "Request body is required" });
        }

        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return BadRequest(new { errors = validationErrors });
        }

        try
        {
            var plan = await _planAdministrationService.UpdatePlanAsync(
                planId,
                new PlanUpdateCommand(request!.Name, request.Description, request.DailyRequestLimit, request.IsPaid),
                RequestCancellationToken).ConfigureAwait(false);
            return plan is null ? NotFound(new { error = "Plan not found" }) : Ok(plan);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error updating plan");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    [HttpDelete("{planId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePlanAsync(string planId)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            return BadRequest(new { error = "Plan ID is required" });
        }

        try
        {
            var result = await _planAdministrationService.DeletePlanAsync(planId, RequestCancellationToken).ConfigureAwait(false);
            return !result.Found ? NotFound(new { error = "Plan not found" }) :
                !result.Deleted ? StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to delete plan" }) :
                NoContent();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error deleting plan");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    private static List<string> Validate(CreatePlanRequest? request)
    {
        var errors = new List<string>();
        if (request is null)
        {
            errors.Add("Request body is required");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.Name)) errors.Add("Plan name is required");
        else if (request.Name.Length > 100) errors.Add("Plan name cannot exceed 100 characters");
        if (request.DailyRequestLimit < 1) errors.Add("Daily request limit must be at least 1");
        else if (request.DailyRequestLimit > 10000) errors.Add("Daily request limit cannot exceed 10000");
        return errors;
    }

    private static List<string> Validate(UpdatePlanRequest? request)
    {
        var errors = new List<string>();
        if (request is null)
        {
            errors.Add("Request body is required");
            return errors;
        }

        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name.Length > 100) errors.Add("Plan name cannot exceed 100 characters");
        if (request.DailyRequestLimit.HasValue && (request.DailyRequestLimit < 1 || request.DailyRequestLimit > 10000)) errors.Add("Daily request limit must be between 1 and 10000");
        return errors;
    }

    private CancellationToken RequestCancellationToken => ControllerContext.HttpContext?.RequestAborted ?? CancellationToken.None;
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "DTOs must be public for API documentation")]
public class CreatePlanRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    [JsonRequired] public int DailyRequestLimit { get; set; } = 100;
    [JsonRequired] public bool IsPaid { get; set; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "DTOs must be public for API documentation")]
public class UpdatePlanRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? DailyRequestLimit { get; set; }
    public bool? IsPaid { get; set; }
}
