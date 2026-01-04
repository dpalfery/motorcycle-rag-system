using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Models.DTOs;

using System.Net.Mime;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Provides RESTful endpoints for administrative web source management.
/// </summary>
[ApiController]
[Route("api/admin/web-sources")]
[Authorize(Policy = "DataAdmin")]
public sealed class WebSourcesAdminController : ControllerBase {
    private readonly WebSourceRegistryService _webSourceRegistryService;
    private readonly ILogger<WebSourcesAdminController> _logger;

    public WebSourcesAdminController(
        WebSourceRegistryService webSourceRegistryService,
        ILogger<WebSourcesAdminController> logger) {
        _webSourceRegistryService = webSourceRegistryService ?? throw new ArgumentNullException(nameof(webSourceRegistryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets all registered web sources.
    /// </summary>
    /// <returns>Array of all web sources</returns>
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(WebSource[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllWebSourcesAsync() {
        try {
            var sources = await _webSourceRegistryService.GetAllSourcesAsync();
            _logger.LogInformation("Admin retrieved {Count} web sources", sources.Length);
            return Ok(sources);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving web sources");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Gets a web source by ID.
    /// </summary>
    /// <param name="webSourceId">Web source ID</param>
    /// <returns>Web source details</returns>
    [HttpGet("{webSourceId}")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(WebSource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWebSourceByIdAsync(int webSourceId) {
        if (webSourceId <= 0) {
            return BadRequest(new { error = "Web source ID must be greater than 0" });
        }

        try {
            var source = await _webSourceRegistryService.GetSourceByIdAsync(webSourceId);
            if (source == null) {
                _logger.LogWarning("Web source {WebSourceId} not found", webSourceId);
                return NotFound(new { error = "Web source not found" });
            }

            _logger.LogInformation("Admin retrieved web source {WebSourceId}", webSourceId);
            return Ok(source);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving web source {WebSourceId}", webSourceId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Creates a new web source.
    /// </summary>
    /// <param name="request">Web source creation request</param>
    /// <returns>Created web source</returns>
    [HttpPost]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(WebSource), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateWebSourceAsync([FromBody] CreateWebSourceRequest request) {
        if (request == null) {
            return BadRequest(new { error = "Request body is required" });
        }

        var errors = ValidateCreateRequest(request);
        if (errors.Count > 0) {
            return BadRequest(new { errors });
        }

        try {
            var webSource = new WebSource {
                Url = request.Url,
                Name = request.Name,
                Description = request.Description ?? string.Empty,
                IsEnabled = request.IsEnabled ?? true,
                TrustTier = request.TrustTier ?? 3,
                CrawlFrequencyHours = request.CrawlFrequencyHours ?? 24,
                IncludeInSearch = request.IncludeInSearch ?? true,
                MaxCrawlDepth = request.MaxCrawlDepth ?? 2,
                CreatedDate = DateTime.UtcNow
            };

            var createdSource = await _webSourceRegistryService.AddWebSourceAsync(webSource);
            _logger.LogInformation("Admin created web source {WebSourceId} with URL {Url}", createdSource.Id, SanitizeLogValue(createdSource.Url));

            return Created($"/api/admin/web-sources/{createdSource.Id}", createdSource);
        }
        catch (InvalidOperationException ex) {
            _logger.LogWarning(ex, "Conflict creating web source with URL {Url}", SanitizeLogValue(request.Url));
            return Conflict(new { error = "A resource with this URL already exists" });
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error creating web source with URL {Url}", SanitizeLogValue(request.Url));
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Updates an existing web source.
    /// </summary>
    /// <param name="webSourceId">Web source ID</param>
    /// <param name="request">Web source update request</param>
    /// <returns>Updated web source</returns>
    [HttpPut("{webSourceId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(WebSource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateWebSourceAsync(int webSourceId, [FromBody] UpdateWebSourceRequest request) {
        if (webSourceId <= 0) {
            return BadRequest(new { error = "Web source ID must be greater than 0" });
        }

        if (request == null) {
            return BadRequest(new { error = "Request body is required" });
        }

        var errors = ValidateUpdateRequest(request);
        if (errors.Count > 0) {
            return BadRequest(new { errors });
        }

        try {
            var existingSource = await _webSourceRegistryService.GetSourceByIdAsync(webSourceId);
            if (existingSource == null) {
                _logger.LogWarning("Web source {WebSourceId} not found for update", webSourceId);
                return NotFound(new { error = "Web source not found" });
            }

            // Update only provided fields
            if (!string.IsNullOrWhiteSpace(request.Url)) {
                existingSource.Url = request.Url;
            }

            if (!string.IsNullOrWhiteSpace(request.Name)) {
                existingSource.Name = request.Name;
            }

            if (!string.IsNullOrWhiteSpace(request.Description)) {
                existingSource.Description = request.Description;
            }

            if (request.IsEnabled.HasValue) {
                existingSource.IsEnabled = request.IsEnabled.Value;
            }

            if (request.TrustTier.HasValue) {
                existingSource.TrustTier = request.TrustTier.Value;
            }

            if (request.CrawlFrequencyHours.HasValue) {
                existingSource.CrawlFrequencyHours = request.CrawlFrequencyHours.Value;
            }

            if (request.IncludeInSearch.HasValue) {
                existingSource.IncludeInSearch = request.IncludeInSearch.Value;
            }

            if (request.MaxCrawlDepth.HasValue) {
                existingSource.MaxCrawlDepth = request.MaxCrawlDepth.Value;
            }

            var updatedSource = await _webSourceRegistryService.UpdateWebSourceAsync(existingSource);
            _logger.LogInformation("Admin updated web source {WebSourceId}", webSourceId);

            return Ok(updatedSource);
        }
        catch (InvalidOperationException ex) {
            _logger.LogWarning(ex, "Conflict updating web source {WebSourceId}", webSourceId);
            return Conflict(new { error = "Unable to update this resource" });
        }
        catch (ArgumentException ex) {
            _logger.LogWarning(ex, "Invalid argument updating web source {WebSourceId}", webSourceId);
            return BadRequest(new { error = "Invalid request parameters" });
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error updating web source {WebSourceId}", webSourceId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Deletes a web source.
    /// </summary>
    /// <param name="webSourceId">Web source ID</param>
    /// <returns>No content on success</returns>
    [HttpDelete("{webSourceId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteWebSourceAsync(int webSourceId) {
        if (webSourceId <= 0) {
            return BadRequest(new { error = "Web source ID must be greater than 0" });
        }

        try {
            var existingSource = await _webSourceRegistryService.GetSourceByIdAsync(webSourceId);
            if (existingSource == null) {
                _logger.LogWarning("Web source {WebSourceId} not found for deletion", webSourceId);
                return NotFound(new { error = "Web source not found" });
            }

            await _webSourceRegistryService.RemoveWebSourceAsync(webSourceId);
            _logger.LogInformation("Admin deleted web source {WebSourceId}", webSourceId);

            return NoContent();
        }
        catch (InvalidOperationException ex) {
            _logger.LogWarning(ex, "Error deleting web source {WebSourceId}", webSourceId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Unable to delete this resource" });
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error deleting web source {WebSourceId}", webSourceId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Validates create web source request.
    /// </summary>
    private List<string> ValidateCreateRequest(CreateWebSourceRequest request) {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Url)) {
            errors.Add("URL is required");
        }
        else if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _)) {
            errors.Add("URL must be a valid absolute URI");
        }

        if (string.IsNullOrWhiteSpace(request.Name)) {
            errors.Add("Name is required");
        }
        else if (request.Name.Length > 255) {
            errors.Add("Name cannot exceed 255 characters");
        }

        if (request.Description != null && request.Description.Length > 1000) {
            errors.Add("Description cannot exceed 1000 characters");
        }

        if (request.TrustTier.HasValue && (request.TrustTier < 1 || request.TrustTier > 5)) {
            errors.Add("Trust tier must be between 1 and 5");
        }

        if (request.CrawlFrequencyHours.HasValue && request.CrawlFrequencyHours < 1) {
            errors.Add("Crawl frequency must be at least 1 hour");
        }

        if (request.MaxCrawlDepth.HasValue && (request.MaxCrawlDepth < 1 || request.MaxCrawlDepth > 10)) {
            errors.Add("Max crawl depth must be between 1 and 10");
        }

        return errors;
    }

    /// <summary>
    /// Validates update web source request.
    /// </summary>
    private List<string> ValidateUpdateRequest(UpdateWebSourceRequest request) {
        var errors = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.Url) && !Uri.TryCreate(request.Url, UriKind.Absolute, out _)) {
            errors.Add("URL must be a valid absolute URI");
        }

        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name.Length > 255) {
            errors.Add("Name cannot exceed 255 characters");
        }

        if (request.Description != null && request.Description.Length > 1000) {
            errors.Add("Description cannot exceed 1000 characters");
        }

        if (request.TrustTier.HasValue && (request.TrustTier < 1 || request.TrustTier > 5)) {
            errors.Add("Trust tier must be between 1 and 5");
        }

        if (request.CrawlFrequencyHours.HasValue && request.CrawlFrequencyHours < 1) {
            errors.Add("Crawl frequency must be at least 1 hour");
        }

        if (request.MaxCrawlDepth.HasValue && (request.MaxCrawlDepth < 1 || request.MaxCrawlDepth > 10)) {
            errors.Add("Max crawl depth must be between 1 and 10");
        }

        return errors;
    }

    /// <summary>
    /// Sanitizes a value for logging to prevent leaking sensitive data.
    /// </summary>
    private string SanitizeLogValue(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "[empty]";
        }

        const int maxLogLength = 48;
        if (value.Length > maxLogLength) {
            return value.Substring(0, maxLogLength) + "...";
        }

        return value;
    }
}

/// <summary>
/// Create web source request model
/// </summary>
public class CreateWebSourceRequest {
    /// <summary>
    /// URL of the web source
    /// </summary>
    [Required]
    [Url]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the web source
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the web source
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the web source is enabled (default: true)
    /// </summary>
    public bool? IsEnabled { get; set; }

    /// <summary>
    /// Trust tier for the web source (1-5, default: 3)
    /// </summary>
    public int? TrustTier { get; set; }

    /// <summary>
    /// Crawl frequency in hours (default: 24)
    /// </summary>
    public int? CrawlFrequencyHours { get; set; }

    /// <summary>
    /// Whether to include this source in web search results (default: true)
    /// </summary>
    public bool? IncludeInSearch { get; set; }

    /// <summary>
    /// Maximum depth to crawl (default: 2)
    /// </summary>
    public int? MaxCrawlDepth { get; set; }
}

/// <summary>
/// Update web source request model
/// </summary>
public class UpdateWebSourceRequest {
    /// <summary>
    /// URL of the web source
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Display name for the web source
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Description of the web source
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the web source is enabled
    /// </summary>
    public bool? IsEnabled { get; set; }

    /// <summary>
    /// Trust tier for the web source (1-5)
    /// </summary>
    public int? TrustTier { get; set; }

    /// <summary>
    /// Crawl frequency in hours
    /// </summary>
    public int? CrawlFrequencyHours { get; set; }

    /// <summary>
    /// Whether to include this source in web search results
    /// </summary>
    public bool? IncludeInSearch { get; set; }

    /// <summary>
    /// Maximum depth to crawl
    /// </summary>
    public int? MaxCrawlDepth { get; set; }
}
