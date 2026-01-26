using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Controller for pipeline monitoring operations including health checks
/// </summary>
[ApiController]
[Route("api/pipeline-monitoring")]
[Produces("application/json")]
[Authorize(Policy = "Admin")] // Require Admin policy for monitoring operations
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:Uri properties should not be strings", Justification = "DTOs for API")]
public class PipelineMonitoringController : ControllerBase
{
    private readonly IPipelineMonitoringService _monitoringService;
    private readonly ILogger<PipelineMonitoringController> _logger;

    public PipelineMonitoringController(
        IPipelineMonitoringService monitoringService,
        ILogger<PipelineMonitoringController> logger)
    {
        _monitoringService = monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get pipeline health status
    /// </summary>
    /// <returns>Pipeline health status</returns>
    [HttpGet("health")]
    [ProducesResponseType(typeof(PipelineHealthStatus), 200)]
    public async Task<IActionResult> GetPipelineHealthAsync()
    {
        try
        {
            var health = await _monitoringService.GetHealthStatusAsync();
            return Ok(health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pipeline health status");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while retrieving pipeline health status",
                Status = 500
            });
        }
    }
}