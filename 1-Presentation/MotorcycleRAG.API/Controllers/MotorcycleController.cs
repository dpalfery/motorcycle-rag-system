using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;
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
        
        // Additional business validation
        var validationResult = ValidateQueryRequest(request);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Query validation failed: {Errors}", string.Join(", ", validationResult.Errors));
            return BadRequest(new { errors = validationResult.Errors });
        }

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

    /// <summary>
    /// Validates query request with business rules
    /// </summary>
    /// <param name="request">Query request to validate</param>
    /// <returns>Validation result</returns>
    private ValidationResult ValidateQueryRequest(MotorcycleQueryRequest request)
    {
        var errors = new List<string>();

        // Validate query length and content
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            errors.Add("Query cannot be empty");
        }
        else if (request.Query.Length < 3)
        {
            errors.Add("Query is too short (minimum 3 characters)");
        }
        else if (request.Query.Length > 1000)
        {
            errors.Add("Query is too long (maximum 1000 characters)");
        }

        // Validate preferences
        if (request.Preferences != null)
        {
            if (request.Preferences.MaxResults <= 0)
            {
                errors.Add("MaxResults must be greater than 0");
            }
            else if (request.Preferences.MaxResults > 100)
            {
                errors.Add("MaxResults cannot exceed 100");
            }

            if (request.Preferences.MinRelevanceScore < 0 || request.Preferences.MinRelevanceScore > 1)
            {
                errors.Add("MinRelevanceScore must be between 0 and 1");
            }

            // Validate preferred sources if any
            if (request.Preferences.PreferredSources != null)
            {
                foreach (var source in request.Preferences.PreferredSources)
                {
                    if (string.IsNullOrWhiteSpace(source))
                    {
                        errors.Add("PreferredSources cannot contain empty values");
                        break;
                    }
                    else if (source.Length > 100)
                    {
                        errors.Add("PreferredSources values cannot exceed 100 characters");
                        break;
                    }
                }
            }
        }

        // Validate user ID
        if (!string.IsNullOrWhiteSpace(request.UserId) && request.UserId.Length > 100)
        {
            errors.Add("UserId cannot exceed 100 characters");
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }

    /// <summary>
    /// Validation result
    /// </summary>
    private class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
