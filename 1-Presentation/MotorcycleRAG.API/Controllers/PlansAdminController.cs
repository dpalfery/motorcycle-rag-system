using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MotorcycleRAG.Contracts.Interfaces;

using System.Net.Mime;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Provides RESTful endpoints for administrative plan management.
/// </summary>
[ApiController]
[Route("api/admin/plans")]
[Authorize(Policy = "Admin")]
public sealed class PlansAdminController : ControllerBase {
    private readonly IPlanRepository _planRepository;
    private readonly IUserAdminService _userAdminService;
    private readonly ILogger<PlansAdminController> _logger;

    public PlansAdminController(
        IPlanRepository planRepository,
        IUserAdminService userAdminService,
        ILogger<PlansAdminController> logger) {
        _planRepository = planRepository ?? throw new ArgumentNullException(nameof(planRepository));
        _userAdminService = userAdminService ?? throw new ArgumentNullException(nameof(userAdminService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets all available plans.
    /// </summary>
    /// <returns>List of all plans</returns>
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(MotorcycleRAG.Contracts.Models.DTOs.UserPlan[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllPlansAsync() {
        try {
            var plans = await _planRepository.GetAllPlansAsync();
            _logger.LogInformation("Admin retrieved {Count} plans", plans.Length);
            return Ok(plans);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving plans");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Gets a plan by ID.
    /// </summary>
    /// <param name="planId">Plan ID</param>
    /// <returns>Plan details</returns>
    [HttpGet("{planId}")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(MotorcycleRAG.Contracts.Models.DTOs.UserPlan), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanByIdAsync(string planId) {
        if (string.IsNullOrWhiteSpace(planId)) {
            return BadRequest(new { error = "Plan ID is required" });
        }

        try {
            var plan = await _planRepository.GetPlanByIdAsync(planId);
            if (plan == null) {
                _logger.LogWarning("Plan {PlanId} not found", planId);
                return NotFound(new { error = "Plan not found" });
            }

            _logger.LogInformation("Admin retrieved plan {PlanId}", planId);
            return Ok(plan);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving plan {PlanId}", planId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Creates a new plan.
    /// </summary>
    /// <param name="request">Plan creation request</param>
    /// <returns>Created plan</returns>
    [HttpPost]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(MotorcycleRAG.Contracts.Models.DTOs.UserPlan), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreatePlanAsync([FromBody] CreatePlanRequest request) {
        if (request == null) {
            return BadRequest(new { error = "Request body is required" });
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Name)) {
            errors.Add("Plan name is required");
        }
        else if (request.Name.Length > 100) {
            errors.Add("Plan name cannot exceed 100 characters");
        }

        if (request.DailyRequestLimit < 1) {
            errors.Add("Daily request limit must be at least 1");
        }
        else if (request.DailyRequestLimit > 10000) {
            errors.Add("Daily request limit cannot exceed 10000");
        }

        if (errors.Count > 0) {
            return BadRequest(new { errors });
        }

        try {
            var plan = new MotorcycleRAG.Contracts.Models.DTOs.UserPlan {
                Id = Guid.NewGuid().ToString(),
                Name = request.Name,
                Description = request.Description ?? string.Empty,
                DailyRequestLimit = request.DailyRequestLimit,
                IsPaid = request.IsPaid,
                CreatedDate = DateTime.UtcNow
            };

            var createdPlan = await _planRepository.CreatePlanAsync(plan);
            _logger.LogInformation("Admin created plan {PlanId} with name {PlanName}", createdPlan.Id, createdPlan.Name);

            // Avoid route generation failures under test hosts by returning an explicit location.
            return Created($"/api/admin/plans/{createdPlan.Id}", createdPlan);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error creating plan");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Updates an existing plan.
    /// </summary>
    /// <param name="planId">Plan ID</param>
    /// <param name="request">Plan update request</param>
    /// <returns>Updated plan</returns>
    [HttpPut("{planId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(MotorcycleRAG.Contracts.Models.DTOs.UserPlan), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePlanAsync(string planId, [FromBody] UpdatePlanRequest request) {
        if (string.IsNullOrWhiteSpace(planId)) {
            return BadRequest(new { error = "Plan ID is required" });
        }

        if (request == null) {
            return BadRequest(new { error = "Request body is required" });
        }

        var errors = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name.Length > 100) {
            errors.Add("Plan name cannot exceed 100 characters");
        }

        if (request.DailyRequestLimit.HasValue && (request.DailyRequestLimit < 1 || request.DailyRequestLimit > 10000)) {
            errors.Add("Daily request limit must be between 1 and 10000");
        }

        if (errors.Count > 0) {
            return BadRequest(new { errors });
        }

        try {
            var plan = await _planRepository.GetPlanByIdAsync(planId);
            if (plan == null) {
                _logger.LogWarning("Plan {PlanId} not found for update", planId);
                return NotFound(new { error = "Plan not found" });
            }

            if (!string.IsNullOrWhiteSpace(request.Name)) {
                plan.Name = request.Name;
            }

            if (!string.IsNullOrWhiteSpace(request.Description)) {
                plan.Description = request.Description;
            }

            if (request.DailyRequestLimit.HasValue) {
                plan.DailyRequestLimit = request.DailyRequestLimit.Value;
            }

            var success = await _planRepository.UpdatePlanAsync(plan);
            if (!success) {
                _logger.LogError("Failed to update plan {PlanId}", planId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to update plan" });
            }

            _logger.LogInformation("Admin updated plan {PlanId}", planId);
            return Ok(plan);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error updating plan {PlanId}", planId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Deletes a plan.
    /// </summary>
    /// <param name="planId">Plan ID</param>
    /// <returns>No content on success</returns>
    [HttpDelete("{planId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePlanAsync(string planId) {
        if (string.IsNullOrWhiteSpace(planId)) {
            return BadRequest(new { error = "Plan ID is required" });
        }

        try {
            var plan = await _planRepository.GetPlanByIdAsync(planId);
            if (plan == null) {
                _logger.LogWarning("Plan {PlanId} not found for deletion", planId);
                return NotFound(new { error = "Plan not found" });
            }

            var success = await _planRepository.DeletePlanAsync(planId);
            if (!success) {
                _logger.LogError("Failed to delete plan {PlanId}", planId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to delete plan" });
            }

            _logger.LogInformation("Admin deleted plan {PlanId}", planId);
            return NoContent();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error deleting plan {PlanId}", planId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }
}

/// <summary>
/// Create plan request model
/// </summary>
public class CreatePlanRequest {
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DailyRequestLimit { get; set; } = 100;
    public bool IsPaid { get; set; } = false;
}

/// <summary>
/// Update plan request model
/// </summary>
public class UpdatePlanRequest {
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? DailyRequestLimit { get; set; }
    public bool? IsPaid { get; set; }
}