using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using System.Net.Mime;
using System.Diagnostics;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Provides RESTful endpoints for querying motorcycle information through the Retrieval-Augmented Generation (RAG) pipeline.
/// </summary>
[ApiController]
[Route("api/motorcycles")]
[Authorize] // Default authorization for all endpoints
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
public sealed class MotorcycleController : ControllerBase {
    private readonly IMotorcycleRagService _ragService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPlanPolicyService _planPolicyService;
    private readonly IUsageTrackingService _usageTrackingService;
    private readonly ILogger<MotorcycleController> _logger;

    public MotorcycleController(
        IMotorcycleRagService ragService,
        ICurrentUserService currentUserService,
        IPlanPolicyService planPolicyService,
        IUsageTrackingService usageTrackingService,
        ILogger<MotorcycleController> logger) {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _planPolicyService = planPolicyService ?? throw new ArgumentNullException(nameof(planPolicyService));
        _usageTrackingService = usageTrackingService ?? throw new ArgumentNullException(nameof(usageTrackingService));
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
    public async Task<IActionResult> QueryAsync([FromBody] MotorcycleQueryRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        // The [ApiController] attribute automatically validates the model state and returns 400 if invalid.

        // Get current user
        var userId = _currentUserService.UserId;
        if (string.IsNullOrWhiteSpace(userId)) {
            _logger.LogWarning("Query attempt without authenticated user");
            return Unauthorized(new { error = "Authentication required" });
        }

        // Check daily request limit
        var hasExceededLimit = await _planPolicyService.HasExceededDailyLimitAsync(userId);
        if (hasExceededLimit) {
            var remaining = await _planPolicyService.GetRemainingDailyRequestsAsync(userId);
            _logger.LogWarning("User {UserId} exceeded daily limit", userId);
            return StatusCode(StatusCodes.Status429TooManyRequests, new {
                error = "Daily request limit exceeded",
                remainingRequests = remaining,
                resetTime = DateTime.UtcNow.Date.AddDays(1)
            });
        }

        // Additional business validation
        var validationResult = ValidateQueryRequest(request);
        if (!validationResult.IsValid) {
            _logger.LogWarning("Query validation failed for user {UserId}: {Errors}", userId, string.Join(", ", validationResult.Errors));
            return BadRequest(new { errors = validationResult.Errors });
        }

        var stopwatch = Stopwatch.StartNew();
        try {
            var response = await _ragService.QueryAsync(request);
            stopwatch.Stop();

            // Record successful usage
            await _usageTrackingService.RecordSuccessAsync(
                userId: userId,
                endpoint: "/api/motorcycles/query",
                httpMethod: "POST",
                queryId: response.QueryId,
                durationMs: stopwatch.ElapsedMilliseconds,
                callerIp: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: GetRequestUserAgent());

            return Ok(response);
        }
        catch (ArgumentException ex) {
            // Expected validation / domain errors → 400 Bad Request
            stopwatch.Stop();
            _logger.LogWarning(ex, "Validation error processing motorcycle query for user {UserId}", userId);

            // Record failed usage
            await _usageTrackingService.RecordFailureAsync(
                userId: userId,
                endpoint: "/api/motorcycles/query",
                httpMethod: "POST",
                statusCode: StatusCodes.Status400BadRequest,
                durationMs: stopwatch.ElapsedMilliseconds,
                callerIp: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: GetRequestUserAgent());

            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex) {
            // Unexpected failure → 500 Internal Server Error
            stopwatch.Stop();
            _logger.LogError(ex, "Unhandled exception processing motorcycle query for user {UserId}", userId);

            // Record failed usage
            await _usageTrackingService.RecordFailureAsync(
                userId: userId,
                endpoint: "/api/motorcycles/query",
                httpMethod: "POST",
                statusCode: StatusCodes.Status500InternalServerError,
                durationMs: stopwatch.ElapsedMilliseconds,
                callerIp: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: GetRequestUserAgent());

            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred." });
        }
    }

    /// <summary>
    /// Returns a lightweight health indicator for the RAG system.
    /// </summary>
    [HttpGet("health")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(HealthCheckResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> HealthAsync() {
        var result = await _ragService.GetHealthAsync();
        return Ok(result);
    }

    /// <summary>
    /// Validates query request with business rules
    /// </summary>
    /// <param name="request">Query request to validate</param>
    /// <returns>Validation result</returns>
    private ValidationResult ValidateQueryRequest(MotorcycleQueryRequest request) {
        var errors = new List<string>();

        // Validate query length and content
        if (string.IsNullOrWhiteSpace(request.Query)) {
            errors.Add("Query cannot be empty");
        }
        else if (request.Query.Length < 3) {
            errors.Add("Query is too short (minimum 3 characters)");
        }
        else if (request.Query.Length > 1000) {
            errors.Add("Query is too long (maximum 1000 characters)");
        }

        // Validate preferences
        if (request.Preferences != null) {
            if (request.Preferences.MaxResults <= 0) {
                errors.Add("MaxResults must be greater than 0");
            }
            else if (request.Preferences.MaxResults > 100) {
                errors.Add("MaxResults cannot exceed 100");
            }

            if (request.Preferences.MinRelevanceScore < 0 || request.Preferences.MinRelevanceScore > 1) {
                errors.Add("MinRelevanceScore must be between 0 and 1");
            }

            // Validate preferred sources if any
            if (request.Preferences.PreferredSources != null) {
                const int maxPreferredSources = 10;
                if (request.Preferences.PreferredSources.Count > maxPreferredSources) {
                    errors.Add($"PreferredSources cannot exceed {maxPreferredSources} items");
                }

                foreach (var source in request.Preferences.PreferredSources) {
                    if (string.IsNullOrWhiteSpace(source)) {
                        errors.Add("PreferredSources cannot contain empty values");
                    }
                    else if (source.Length > 100) {
                        errors.Add("PreferredSources values cannot exceed 100 characters");
                    }
                    else if (!System.Text.RegularExpressions.Regex.IsMatch(source, @"^[a-zA-Z0-9\-_]+$")) {
                        errors.Add("PreferredSources values must contain only alphanumeric characters, hyphens, or underscores");
                    }
                }
            }
        }

        // Validate user ID
        if (!string.IsNullOrWhiteSpace(request.UserId) && request.UserId.Length > 100) {
            errors.Add("UserId cannot exceed 100 characters");
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }

    /// <summary>
    /// Extracts User-Agent from request headers safely
    /// </summary>
    private string GetRequestUserAgent() {
        // Access User-Agent through the HttpContext which is properly managed by ASP.NET Core
        return HttpContext?.Request?.Headers.UserAgent.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Validation result
    /// </summary>
    private class ValidationResult {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
