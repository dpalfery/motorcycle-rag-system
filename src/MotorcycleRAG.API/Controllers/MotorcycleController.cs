using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using System.Net.Mime;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Provides RESTful endpoints for querying motorcycle information through the Retrieval-Augmented Generation (RAG) pipeline.
/// </summary>
[ApiController]
[Route("api/motorcycles")]
public sealed class MotorcycleController : ControllerBase
{
    private readonly IMotorcycleRAGService _ragService;
    private readonly ILogger<MotorcycleController> _logger;

    public MotorcycleController(IMotorcycleRAGService ragService, ILogger<MotorcycleController> logger)
    {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes a motorcycle-related natural language query and returns an AI-generated answer together with supporting sources.
    /// </summary>
    /// <param name="request">Query request body.</param>
    /// <returns>RAG response containing the generated answer, sources and metrics.</returns>
    [HttpPost("query")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(MotorcycleQueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> QueryAsync([FromBody] MotorcycleQueryRequest request)
    {
        // The [ApiController] attribute automatically validates the model state and returns 400 if invalid.
        try
        {
            var response = await _ragService.QueryAsync(request);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            // Expected validation / domain errors → 400 Bad Request
            _logger.LogWarning(ex, "Validation error processing motorcycle query");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            // Unexpected failure → 500 Internal Server Error
            _logger.LogError(ex, "Unhandled exception processing motorcycle query");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred." });
        }
    }

    /// <summary>
    /// Returns a lightweight health indicator for the RAG system.
    /// </summary>
    [HttpGet("health")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(HealthCheckResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> HealthAsync()
    {
        var result = await _ragService.GetHealthAsync();
        return Ok(result);
    }
}