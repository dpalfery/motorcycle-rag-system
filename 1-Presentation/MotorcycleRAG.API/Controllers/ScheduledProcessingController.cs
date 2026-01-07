using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Controller for scheduled processing operations
/// </summary>
[ApiController]
[Route("api/scheduled-processing")]
[Produces("application/json")]
[Authorize(Policy = "DataAdmin")] // Require DataAdmin role for scheduled processing operations
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:Uri properties should not be strings", Justification = "DTOs for API")]
public class ScheduledProcessingController : ControllerBase
{
    private readonly IScheduledPipelineService _scheduledService;
    private readonly ILogger<ScheduledProcessingController> _logger;

    public ScheduledProcessingController(
        IScheduledPipelineService scheduledService,
        ILogger<ScheduledProcessingController> logger)
    {
        _scheduledService = scheduledService ?? throw new ArgumentNullException(nameof(scheduledService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Execute immediate scheduled processing run
    /// </summary>
    /// <returns>Pipeline execution result</returns>
    [HttpPost("execute")]
    [ProducesResponseType(typeof(PipelineExecutionResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<IActionResult> ExecuteScheduledProcessingAsync()
    {
        try
        {
            var result = await _scheduledService.ExecuteImmediateRunAsync(HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Scheduled processing execution was cancelled");
            return StatusCode(499, new ProblemDetails
            {
                Title = "Request cancelled",
                Detail = "The scheduled processing execution was cancelled by the client",
                Status = 499
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing scheduled processing");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while executing scheduled processing",
                Status = 500
            });
        }
    }

    /// <summary>
    /// Get scheduled processing statistics
    /// </summary>
    /// <returns>Scheduled processing stats</returns>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(ScheduledProcessingStats), 200)]
    public async Task<IActionResult> GetScheduledProcessingStatsAsync()
    {
        try
        {
            var stats = await _scheduledService.GetProcessingStatsAsync();
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting scheduled processing stats");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while retrieving scheduled processing stats",
                Status = 500
            });
        }
    }
}