using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;
using System.Net.Mime;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Provides RESTful endpoints for MCP (Model Context Protocol) tool configuration management.
/// Restricted to admins only.
/// Follows Clean Architecture: depends on Application layer (IToolConfigurationService),
/// not directly on Persistence layer.
/// </summary>
[ApiController]
[Route("api/admin/mcp-tools")]
[Authorize(Policy = "DataAdmin")]
public sealed class McpAdminController : ControllerBase {
    private readonly IToolConfigurationService _configService;
    private readonly ILogger<McpAdminController> _logger;
    private readonly ICurrentUserService _currentUserService;

    public McpAdminController(
        IToolConfigurationService configService,
        ILogger<McpAdminController> logger,
        ICurrentUserService currentUserService) {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    /// <summary>
    /// Gets all MCP tool configurations.
    /// </summary>
    /// <returns>Array of all MCP tool configurations</returns>
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(McpToolConfigurationDto[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllToolsAsync() {
        try {
            var configs = await _configService.GetAllToolsAsync();
            var dtos = configs.Select(MapToDto).ToArray();

            _logger.LogInformation("Admin retrieved {Count} MCP tool configurations", dtos.Length);
            return Ok(dtos);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving MCP tool configurations");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred retrieving tool configurations" });
        }
    }

    /// <summary>
    /// Gets a specific MCP tool configuration by ID.
    /// </summary>
    /// <param name="toolId">Tool ID</param>
    /// <returns>MCP tool configuration details</returns>
    [HttpGet("{toolId}")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(McpToolConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetToolAsync(string toolId) {
        if (string.IsNullOrWhiteSpace(toolId))
            return BadRequest(new { error = "Tool ID must not be empty" });

        try {
            var config = await _configService.GetToolAsync(toolId);
            if (config == null) {
                _logger.LogWarning("MCP tool {ToolId} not found", toolId);
                return NotFound(new { error = "Tool not found" });
            }

            var dto = MapToDto(config);
            _logger.LogInformation("Admin retrieved MCP tool {ToolId}", toolId);
            return Ok(dto);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving MCP tool {ToolId}", toolId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Gets enabled MCP tools (for orchestration).
    /// </summary>
    /// <returns>Array of enabled tools</returns>
    [HttpGet("enabled")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(McpToolConfigurationDto[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetEnabledToolsAsync() {
        try {
            var enabledTools = await _configService.GetEnabledToolsAsync();
            var dtos = enabledTools.Select(MapToDto).ToArray();

            return Ok(dtos);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving enabled MCP tools");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Creates a new MCP tool configuration.
    /// </summary>
    /// <param name="request">Tool configuration request</param>
    /// <returns>Created tool configuration</returns>
    [HttpPost]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(McpToolConfigurationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateToolAsync([FromBody] CreateMcpToolRequest request) {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // SC-003: Manual input validation beyond DataAnnotations
        var validationErrors = ValidateToolInput(request.ToolId, request.Name, request.ServerUrl);
        if (validationErrors.Any())
            return BadRequest(new { errors = validationErrors });

        try {
            // Validate ConfigurationJson if provided
            if (!string.IsNullOrWhiteSpace(request.ConfigurationJson)) {
                try {
                    System.Text.Json.JsonDocument.Parse(request.ConfigurationJson);
                    if (request.ConfigurationJson.Length > 10240) // 10KB limit
                        return BadRequest(new { error = "Configuration JSON exceeds 10KB limit" });
                }
                catch (System.Text.Json.JsonException) {
                    return BadRequest(new { error = "Invalid JSON in ConfigurationJson" });
                }
            }

            var config = new McpToolConfiguration {
                Id = Guid.NewGuid(),
                ToolId = request.ToolId,
                Name = request.Name,
                Description = request.Description,
                ServerUrl = request.ServerUrl,
                ToolType = request.ToolType,
                Version = request.Version,
                IsEnabled = request.IsEnabled ?? true,
                Priority = request.Priority ?? 0,
                TimeoutMs = request.TimeoutMs,
                RetryOnFailure = request.RetryOnFailure ?? true,
                MaxRetries = request.MaxRetries ?? 3,
                ConfigurationJson = request.ConfigurationJson,
                CreatedAt = DateTime.UtcNow
            };

            // Use application service for creation (includes validation and audit)
            var sanitizedUserId = SanitizeUserId(_currentUserService.UserId);
            var savedConfig = await _configService.CreateToolAsync(config, _currentUserService.UserId);

            // Log without exposing user details - SC-004: Sanitized logging
            _logger.LogInformation("Created MCP tool {ToolId} ({ToolName}) by user {UserId}",
                savedConfig.ToolId, savedConfig.Name, sanitizedUserId);

            var dto = MapToDto(savedConfig);
            return CreatedAtAction(nameof(GetToolAsync), new { toolId = savedConfig.ToolId }, dto);
        }
        catch (InvalidOperationException) {
            // Duplicate tool or validation failed - SC-001: Generic error message
            // Note: We don't expose the exception message to prevent information disclosure
            return Conflict(new { error = "Tool already exists" });
        }
        catch (Exception ex) {
            // SC-001: Don't expose exception details in API response
            _logger.LogError(ex, "Error creating MCP tool {ToolId}", request.ToolId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred creating the tool" });
        }
    }

    /// <summary>
    /// Updates an MCP tool configuration.
    /// </summary>
    /// <param name="toolId">Tool ID to update</param>
    /// <param name="request">Update request</param>
    /// <returns>Updated tool configuration</returns>
    [HttpPut("{toolId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(McpToolConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateToolAsync(string toolId, [FromBody] UpdateMcpToolRequest request) {
        if (string.IsNullOrWhiteSpace(toolId))
            return BadRequest(new { error = "Tool ID must not be empty" });

        // SC-003: Validate input length
        if (toolId.Length > 255)
            return BadRequest(new { error = "Tool ID exceeds maximum length of 255 characters" });

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try {
            // Validate ConfigurationJson if provided
            if (!string.IsNullOrWhiteSpace(request.ConfigurationJson)) {
                try {
                    System.Text.Json.JsonDocument.Parse(request.ConfigurationJson);
                    if (request.ConfigurationJson.Length > 10240) // 10KB limit
                        return BadRequest(new { error = "Configuration JSON exceeds 10KB limit" });
                }
                catch (System.Text.Json.JsonException) {
                    return BadRequest(new { error = "Invalid JSON in ConfigurationJson" });
                }
            }

            var existingConfig = await _configService.GetToolAsync(toolId);
            if (existingConfig == null) {
                _logger.LogWarning("MCP tool {ToolId} not found for update", toolId);
                return NotFound(new { error = "Tool not found" });
            }

            // Update properties (only if provided)
            if (!string.IsNullOrWhiteSpace(request.Name)) {
                if (request.Name.Length > 255)
                    return BadRequest(new { error = "Name exceeds maximum length of 255 characters" });
                existingConfig.Name = request.Name;
            }

            if (!string.IsNullOrWhiteSpace(request.Description)) {
                if (request.Description.Length > 1000)
                    return BadRequest(new { error = "Description exceeds maximum length of 1000 characters" });
                existingConfig.Description = request.Description;
            }

            if (!string.IsNullOrWhiteSpace(request.ServerUrl)) {
                if (request.ServerUrl.Length > 500)
                    return BadRequest(new { error = "Server URL exceeds maximum length of 500 characters" });
                existingConfig.ServerUrl = request.ServerUrl;
            }

            if (!string.IsNullOrWhiteSpace(request.ToolType))
                existingConfig.ToolType = request.ToolType;

            if (request.IsEnabled.HasValue)
                existingConfig.IsEnabled = request.IsEnabled.Value;

            if (request.Priority.HasValue)
                existingConfig.Priority = request.Priority.Value;

            if (request.TimeoutMs.HasValue)
                existingConfig.TimeoutMs = request.TimeoutMs.Value;

            if (request.RetryOnFailure.HasValue)
                existingConfig.RetryOnFailure = request.RetryOnFailure.Value;

            if (request.MaxRetries.HasValue)
                existingConfig.MaxRetries = request.MaxRetries.Value;

            if (request.ConfigurationJson != null)
                existingConfig.ConfigurationJson = request.ConfigurationJson;

            // Use application service for update (includes validation and audit)
            var sanitizedUserId = SanitizeUserId(_currentUserService.UserId);
            var updatedConfig = await _configService.UpdateToolAsync(
                toolId,
                existingConfig,
                request.ChangeReason,
                _currentUserService.UserId);

            // Log without exposing user details - SC-004: Sanitized logging
            _logger.LogInformation("Updated MCP tool {ToolId} by user {UserId}", toolId, sanitizedUserId);

            var dto = MapToDto(updatedConfig);
            return Ok(dto);
        }
        catch (InvalidOperationException) {
            // SC-001: Generic error message
            return BadRequest(new { error = "Tool configuration is invalid" });
        }
        catch (Exception ex) {
            // SC-001: Don't expose exception details
            _logger.LogError(ex, "Error updating MCP tool {ToolId}", toolId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred updating the tool" });
        }
    }

    /// <summary>
    /// Enables an MCP tool.
    /// </summary>
    /// <param name="toolId">Tool ID to enable</param>
    /// <returns>Updated tool configuration</returns>
    [HttpPost("{toolId}/enable")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(McpToolConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> EnableToolAsync(string toolId) {
        if (string.IsNullOrWhiteSpace(toolId))
            return BadRequest(new { error = "Tool ID must not be empty" });

        try {
            var config = await _configService.EnableToolAsync(toolId, _currentUserService.UserId);

            _logger.LogInformation("Enabled MCP tool {ToolId}", toolId);

            var dto = MapToDto(config);
            return Ok(dto);
        }
        catch (InvalidOperationException ex) {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error enabling MCP tool {ToolId}", toolId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Disables an MCP tool.
    /// </summary>
    /// <param name="toolId">Tool ID to disable</param>
    /// <param name="request">Disable request with reason</param>
    /// <returns>Updated tool configuration</returns>
    [HttpPost("{toolId}/disable")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(McpToolConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DisableToolAsync(string toolId, [FromBody] DisableMcpToolRequest request) {
        if (string.IsNullOrWhiteSpace(toolId))
            return BadRequest(new { error = "Tool ID must not be empty" });

        try {
            var reason = request?.Reason ?? "Disabled by admin";
            var config = await _configService.DisableToolAsync(toolId, reason, _currentUserService.UserId);

            _logger.LogInformation("Disabled MCP tool {ToolId}", toolId);

            var dto = MapToDto(config);
            return Ok(dto);
        }
        catch (InvalidOperationException ex) {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex) {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error disabling MCP tool {ToolId}", toolId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Deletes an MCP tool configuration.
    /// </summary>
    /// <param name="toolId">Tool ID to delete</param>
    /// <returns>Success response</returns>
    [HttpDelete("{toolId}")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteToolAsync(string toolId) {
        if (string.IsNullOrWhiteSpace(toolId))
            return BadRequest(new { error = "Tool ID must not be empty" });

        try {
            var deleted = await _configService.DeleteToolAsync(toolId, _currentUserService.UserId);

            if (!deleted) {
                _logger.LogWarning("MCP tool {ToolId} not found for deletion", toolId);
                return NotFound(new { error = "Tool not found" });
            }

            var sanitizedUserId = SanitizeUserId(_currentUserService.UserId);
            _logger.LogInformation("Deleted MCP tool {ToolId} by user {UserId}", toolId, sanitizedUserId);

            return NoContent();
        }
        catch (Exception ex) {
            // SC-001: Don't expose exception details
            _logger.LogError(ex, "Error deleting MCP tool {ToolId}", toolId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred deleting the tool" });
        }
    }

    /// <summary>
    /// Gets audit history for a tool configuration.
    /// </summary>
    /// <param name="toolId">Tool ID</param>
    /// <param name="limit">Number of entries to return</param>
    /// <returns>Audit entries</returns>
    [HttpGet("{toolId}/audit")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(ToolConfigurationAuditEntry[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAuditHistoryAsync(string toolId, [FromQuery] int limit = 100) {
        if (string.IsNullOrWhiteSpace(toolId))
            return BadRequest(new { error = "Tool ID must not be empty" });

        if (limit <= 0 || limit > 1000)
            return BadRequest(new { error = "Limit must be between 1 and 1000" });

        try {
            var config = await _configService.GetToolAsync(toolId);
            if (config == null)
                return NotFound(new { error = "Tool not found" });

            var auditEntries = await _configService.GetAuditHistoryAsync(config.Id, limit);
            return Ok(auditEntries);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving audit history for tool {ToolId}", toolId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Gets audit summary statistics.
    /// </summary>
    /// <returns>Audit summary</returns>
    [HttpGet("audit/summary")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(ToolConfigurationAuditSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAuditSummaryAsync() {
        try {
            var summary = await _configService.GetAuditSummaryAsync();
            return Ok(summary);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving audit summary");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Maps McpToolConfiguration to DTO.
    /// </summary>
    private McpToolConfigurationDto MapToDto(McpToolConfiguration config) {
        return new McpToolConfigurationDto {
            Id = config.Id,
            ToolId = config.ToolId,
            Name = config.Name,
            Description = config.Description,
            ServerUrl = config.ServerUrl,
            IsEnabled = config.IsEnabled,
            ToolType = config.ToolType,
            Version = config.Version,
            IsSystemTool = config.IsSystemTool,
            Priority = config.Priority,
            TimeoutMs = config.TimeoutMs,
            RetryOnFailure = config.RetryOnFailure,
            MaxRetries = config.MaxRetries,
            DisabledReason = config.DisabledReason,
            LastConnectionStatus = config.LastConnectionStatus,
            LastTestedAt = config.LastTestedAt,
            ConfigurationJson = config.ConfigurationJson,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt
        };
    }
    /// <summary>
    /// Validates tool input parameters.
    /// SC-003: Manual input validation beyond DataAnnotations.
    /// </summary>
    private List<string> ValidateToolInput(string toolId, string name, string serverUrl) {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(toolId))
            errors.Add("Tool ID is required");
        else if (toolId.Length > 255)
            errors.Add("Tool ID exceeds maximum length of 255 characters");

        if (string.IsNullOrWhiteSpace(name))
            errors.Add("Name is required");
        else if (name.Length > 255)
            errors.Add("Name exceeds maximum length of 255 characters");

        if (string.IsNullOrWhiteSpace(serverUrl))
            errors.Add("Server URL is required");
        else if (serverUrl.Length > 500)
            errors.Add("Server URL exceeds maximum length of 500 characters");

        return errors;
    }

    /// <summary>
    /// Sanitizes user ID for secure logging.
    /// SC-004: Prevents logging of raw user identifiers (OWASP ASVS V7.1).
    /// </summary>
    private string SanitizeUserId(string? userId) {
        if (string.IsNullOrWhiteSpace(userId))
            return "[system]";

        // Hash the user ID using SHA-256 and take first 8 characters for brevity
        using (var sha256 = SHA256.Create()) {
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(userId));
            var hashString = System.Convert.ToBase64String(hash);
            return $"[user:{hashString.Substring(0, 8)}]";
        }
    }
}
