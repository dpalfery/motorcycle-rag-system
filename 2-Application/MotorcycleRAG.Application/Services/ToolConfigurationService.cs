using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.DTOs;
using MotorcycleRAG.Domain.Entities;
using System.Net;
using System.Text.Json;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Application service for managing MCP tool configurations.
/// Orchestrates between the Presentation layer and Persistence layer.
/// Implements validation, security checks, and audit logging.
/// </summary>
public class ToolConfigurationService : IToolConfigurationService {
    /// <summary>
    /// CQ-001: Constants for allowed ports (extracted from magic numbers).
    /// Standard ports for production use.
    /// </summary>
    private static readonly int[] StandardPorts = { 80, 443 };

    /// <summary>
    /// CQ-001: Development ports allowed only on localhost.
    /// SC-002: Restricted to localhost to prevent SSRF bypass.
    /// </summary>
    private static readonly int[] DevelopmentPorts = { 8000, 8001, 8002, 8003, 8004, 8005, 8006, 8007, 8008, 8009 };

    private readonly IToolConfigurationRepository _configRepository;
    private readonly IToolConfigurationAuditRepository _auditRepository;
    private readonly IMcpConfigurationProvider _configProvider;
    private readonly ILogger<ToolConfigurationService> _logger;

    public ToolConfigurationService(
        IToolConfigurationRepository configRepository,
        IToolConfigurationAuditRepository auditRepository,
        IMcpConfigurationProvider configProvider,
        ILogger<ToolConfigurationService> logger) {
        _configRepository = configRepository ?? throw new ArgumentNullException(nameof(configRepository));
        _auditRepository = auditRepository ?? throw new ArgumentNullException(nameof(auditRepository));
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get all tool configurations.
    /// </summary>
    public async Task<McpToolConfiguration[]> GetAllToolsAsync() {
        try {
            var configs = await _configRepository.GetAllAsync();
            return configs.OrderBy(c => c.CreatedAt).ToArray();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving all tool configurations");
            throw;
        }
    }

    /// <summary>
    /// Get a specific tool configuration by tool ID.
    /// </summary>
    public async Task<McpToolConfiguration?> GetToolAsync(string toolId) {
        if (string.IsNullOrWhiteSpace(toolId))
            return null;

        try {
            return await _configRepository.GetByToolIdAsync(toolId);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving tool configuration for {ToolId}", toolId);
            throw;
        }
    }

    /// <summary>
    /// Get all enabled tools for orchestration.
    /// </summary>
    public async Task<McpToolConfiguration[]> GetEnabledToolsAsync() {
        try {
            return await _configRepository.GetEnabledAsync();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving enabled tool configurations");
            throw;
        }
    }

    /// <summary>
    /// Create a new MCP tool configuration.
    /// </summary>
    public async Task<McpToolConfiguration> CreateToolAsync(
        McpToolConfiguration configuration,
        string? userId = null) {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        if (string.IsNullOrWhiteSpace(configuration.ToolId))
            throw new ArgumentException("Tool ID must not be empty", nameof(configuration));

        try {
            // Validate the configuration
            if (!await ValidateToolAsync(configuration))
                throw new InvalidOperationException("Tool configuration validation failed");

            // Check for duplicates
            var existingTool = await _configRepository.GetByToolIdAsync(configuration.ToolId);
            if (existingTool != null)
                throw new InvalidOperationException($"Tool with ID '{configuration.ToolId}' already exists");

            // Set creation timestamp
            configuration.CreatedAt = DateTime.UtcNow;
            if (configuration.Id == Guid.Empty)
                configuration.Id = Guid.NewGuid();

            // Save to database
            var savedConfig = await _configRepository.AddOrUpdateAsync(configuration);

            // Record audit entry
            var auditJson = JsonSerializer.Serialize(savedConfig);
            await _auditRepository.RecordChangeAsync(
                savedConfig.Id,
                savedConfig.ToolId,
                "created",
                beforeJson: null,
                afterJson: auditJson,
                userId,
                "New MCP tool created via admin API");

            _logger.LogInformation(
                "Created MCP tool {ToolId} ({ToolName})",
                savedConfig.ToolId, savedConfig.Name);

            return savedConfig;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error creating MCP tool {ToolId}", configuration.ToolId);
            throw;
        }
    }

    /// <summary>
    /// Update an existing MCP tool configuration.
    /// </summary>
    public async Task<McpToolConfiguration> UpdateToolAsync(
        string toolId,
        McpToolConfiguration configuration,
        string? changeReason = null,
        string? userId = null) {
        if (string.IsNullOrWhiteSpace(toolId))
            throw new ArgumentException("Tool ID must not be empty", nameof(toolId));

        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        try {
            // Get existing configuration
            var existingConfig = await _configRepository.GetByToolIdAsync(toolId);
            if (existingConfig == null)
                throw new InvalidOperationException($"Tool with ID '{toolId}' not found");

            // Validate updated configuration
            if (!await ValidateToolAsync(configuration))
                throw new InvalidOperationException("Updated tool configuration validation failed");

            // Record before state for audit
            var beforeJson = JsonSerializer.Serialize(existingConfig);

            // Update mutable properties
            existingConfig.Name = configuration.Name;
            existingConfig.Description = configuration.Description;
            existingConfig.ServerUrl = configuration.ServerUrl;
            existingConfig.ToolType = configuration.ToolType;
            existingConfig.IsEnabled = configuration.IsEnabled;
            existingConfig.Priority = configuration.Priority;
            existingConfig.TimeoutMs = configuration.TimeoutMs;
            existingConfig.RetryOnFailure = configuration.RetryOnFailure;
            existingConfig.MaxRetries = configuration.MaxRetries;
            existingConfig.ConfigurationJson = configuration.ConfigurationJson;
            existingConfig.UpdatedAt = DateTime.UtcNow;

            // Save to database
            var updatedConfig = await _configRepository.AddOrUpdateAsync(existingConfig);

            // Record audit entry
            var afterJson = JsonSerializer.Serialize(updatedConfig);
            await _auditRepository.RecordChangeAsync(
                updatedConfig.Id,
                updatedConfig.ToolId,
                "updated",
                beforeJson,
                afterJson,
                userId,
                changeReason ?? "Configuration updated via admin API");

            _logger.LogInformation(
                "Updated MCP tool {ToolId}",
                toolId);

            return updatedConfig;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error updating MCP tool {ToolId}", toolId);
            throw;
        }
    }

    /// <summary>
    /// Enable a tool configuration.
    /// </summary>
    public async Task<McpToolConfiguration> EnableToolAsync(string toolId, string? userId = null) {
        if (string.IsNullOrWhiteSpace(toolId))
            throw new ArgumentException("Tool ID must not be empty", nameof(toolId));

        try {
            var config = await _configRepository.GetByToolIdAsync(toolId);
            if (config == null)
                throw new InvalidOperationException($"Tool with ID '{toolId}' not found");

            // Record before state
            var beforeJson = JsonSerializer.Serialize(config);

            // Enable the tool
            config.Enable();

            // Save to database
            var updatedConfig = await _configRepository.AddOrUpdateAsync(config);

            // Record audit entry
            var afterJson = JsonSerializer.Serialize(updatedConfig);
            await _auditRepository.RecordChangeAsync(
                updatedConfig.Id,
                updatedConfig.ToolId,
                "enabled",
                beforeJson,
                afterJson,
                userId,
                "Tool enabled via admin API");

            _logger.LogInformation(
                "Enabled MCP tool {ToolId}",
                toolId);

            return updatedConfig;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error enabling MCP tool {ToolId}", toolId);
            throw;
        }
    }

    /// <summary>
    /// Disable a tool configuration.
    /// </summary>
    public async Task<McpToolConfiguration> DisableToolAsync(
        string toolId,
        string reason,
        string? userId = null) {
        if (string.IsNullOrWhiteSpace(toolId))
            throw new ArgumentException("Tool ID must not be empty", nameof(toolId));

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Disable reason must not be empty", nameof(reason));

        try {
            var config = await _configRepository.GetByToolIdAsync(toolId);
            if (config == null)
                throw new InvalidOperationException($"Tool with ID '{toolId}' not found");

            // Record before state
            var beforeJson = JsonSerializer.Serialize(config);

            // Disable the tool
            config.Disable(reason);

            // Save to database
            var updatedConfig = await _configRepository.AddOrUpdateAsync(config);

            // Record audit entry
            var afterJson = JsonSerializer.Serialize(updatedConfig);
            await _auditRepository.RecordChangeAsync(
                updatedConfig.Id,
                updatedConfig.ToolId,
                "disabled",
                beforeJson,
                afterJson,
                userId,
                reason);

            _logger.LogInformation(
                "Disabled MCP tool {ToolId} - Reason: {Reason}",
                toolId, reason);

            return updatedConfig;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error disabling MCP tool {ToolId}", toolId);
            throw;
        }
    }

    /// <summary>
    /// Validate a tool configuration is accessible and correctly configured.
    /// Performs comprehensive SSRF protection and security checks.
    /// </summary>
    public async Task<bool> ValidateToolAsync(McpToolConfiguration tool) {
        if (tool == null)
            return false;

        // CQ-002: Standardized null check using IsNullOrWhiteSpace
        if (!tool.IsEnabled && string.IsNullOrWhiteSpace(tool.DisabledReason)) {
            _logger.LogWarning("Tool {ToolId} is disabled without reason", tool.ToolId);
            return false;
        }

        if (tool.ServerUrl == null) {
            _logger.LogWarning("Tool {ToolId} has no server URL configured", tool.ToolId);
            return false;
        }

        // Validate URL with SSRF protection
        if (!IsValidMcpServerUrl(tool.ServerUrl?.ToString() ?? string.Empty)) {
            _logger.LogWarning("Tool {ToolId} has invalid or disallowed server URL: {ServerUrl}",
                tool.ToolId, tool.ServerUrl);
            return false;
        }

        // Check timeout configuration
        if (tool.TimeoutMs.HasValue && tool.TimeoutMs <= 0) {
            _logger.LogWarning("Tool {ToolId} has invalid timeout configuration: {Timeout}ms",
                tool.ToolId, tool.TimeoutMs);
            return false;
        }

        // Validate configuration JSON if provided
        if (!string.IsNullOrWhiteSpace(tool.ConfigurationJson)) {
            if (tool.ConfigurationJson.Length > 10240) // 10KB limit
            {
                _logger.LogWarning("Tool {ToolId} configuration JSON exceeds 10KB limit", tool.ToolId);
                return false;
            }

            try {
                JsonDocument.Parse(tool.ConfigurationJson);
            }
            catch (JsonException ex) {
                _logger.LogWarning(ex, "Tool {ToolId} has invalid configuration JSON", tool.ToolId);
                return false;
            }
        }

        _logger.LogDebug("Tool {ToolId} validation passed", tool.ToolId);
        return await Task.FromResult(true);
    }

    /// <summary>
    /// Delete a tool configuration.
    /// </summary>
    public async Task<bool> DeleteToolAsync(string toolId, string? userId = null) {
        if (string.IsNullOrWhiteSpace(toolId))
            throw new ArgumentException("Tool ID must not be empty", nameof(toolId));

        try {
            var config = await _configRepository.GetByToolIdAsync(toolId);
            if (config == null) {
                _logger.LogWarning("Tool {ToolId} not found for deletion", toolId);
                return false;
            }

            // Record before state for audit
            var beforeJson = JsonSerializer.Serialize(config);

            // Delete from database
            var deleted = await _configRepository.DeleteAsync(config.Id);

            if (deleted) {
                // Record audit entry
                await _auditRepository.RecordChangeAsync(
                    config.Id,
                    config.ToolId,
                    "deleted",
                    beforeJson,
                    afterJson: null,
                    userId,
                    "Tool deleted via admin API");

                _logger.LogInformation(
                    "Deleted MCP tool {ToolId}",
                    toolId);
            }

            return deleted;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error deleting MCP tool {ToolId}", toolId);
            throw;
        }
    }

    /// <summary>
    /// Validate MCP server URL to prevent SSRF attacks.
    /// OWASP A01:2021 - Injection / SSRF Protection.
    /// SC-002: Development ports (8000-8009) restricted to localhost only.
    /// </summary>
    private bool IsValidMcpServerUrl(string serverUrl) {
        if (string.IsNullOrWhiteSpace(serverUrl))
            return false;

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
            return false;

        // Only allow HTTP/HTTPS schemes
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        // Block localhost variants
        if (IsLoopbackAddress(uri.Host))
            return false;

        // Block private IP ranges
        if (IsPrivateIpAddress(uri.Host))
            return false;

        // Block reserved and special addresses
        if (IsReservedAddress(uri.Host))
            return false;

        // CQ-001 + SC-002: Port validation using constants
        // Development ports allowed only on localhost, standard ports allowed everywhere
        if (uri.Port > 0) {
            if (DevelopmentPorts.Contains(uri.Port)) {
                // SC-002: Development ports MUST be on localhost only
                if (!IsLocalhostAddress(uri.Host)) {
                    _logger.LogWarning(
                        "Development port {Port} attempted on non-localhost address: {Host}",
                        uri.Port, uri.Host);
                    return false;
                }
            }
            else if (!StandardPorts.Contains(uri.Port)) {
                // CQ-001: Port not in allowed lists
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Check if host is localhost (allowing it for local testing).
    /// SC-002: Used to restrict development ports to localhost only.
    /// </summary>
    private bool IsLocalhostAddress(string host) {
        return host == "localhost" || host == "127.0.0.1" || host == "::1";
    }

    /// <summary>
    /// Check if host is loopback (localhost, 127.0.0.1, ::1, etc.).
    /// SSRF Protection: Blocks all loopback addresses for external communication.
    /// </summary>
    private bool IsLoopbackAddress(string host) {
        return IsLocalhostAddress(host);
    }

    /// <summary>
    /// Check if IP is in private ranges: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16
    /// </summary>
    private bool IsPrivateIpAddress(string host) {
        if (!IPAddress.TryParse(host, out var ipAddress))
            return false;

        var octets = ipAddress.ToString().Split('.');

        // Check for 10.x.x.x
        if (octets.Length == 4 && octets[0] == "10")
            return true;

        // Check for 172.16.x.x to 172.31.x.x
        if (octets.Length == 4 && octets[0] == "172") {
            if (int.TryParse(octets[1], out var secondOctet) && secondOctet >= 16 && secondOctet <= 31)
                return true;
        }

        // Check for 192.168.x.x
        if (octets.Length == 4 && octets[0] == "192" && octets[1] == "168")
            return true;

        return false;
    }

    /// <summary>
    /// Check if address is reserved or special (link-local, multicast, etc.)
    /// </summary>
    private bool IsReservedAddress(string host) {
        if (!IPAddress.TryParse(host, out var ipAddress))
            return false;

        var octets = ipAddress.ToString().Split('.');

        // Check for loopback (127.x.x.x)
        if (octets.Length == 4 && octets[0] == "127")
            return true;

        // Check for link-local (169.254.x.x)
        if (octets.Length == 4 && octets[0] == "169" && octets[1] == "254")
            return true;

        // Check for multicast (224.x.x.x to 239.x.x.x)
        if (octets.Length == 4 && int.TryParse(octets[0], out var firstOctet)) {
            if (firstOctet >= 224 && firstOctet <= 239)
                return true;
        }

        // Check for broadcast (255.255.255.255)
        if (host == "255.255.255.255" || host == "0.0.0.0")
            return true;

        return false;
    }

    /// <summary>
    /// Get audit history for a specific tool configuration.
    /// </summary>
    public async Task<ToolConfigurationAuditEntry[]> GetAuditHistoryAsync(Guid configId, int limit = 100) {
        if (configId == Guid.Empty)
            throw new ArgumentException("Configuration ID must not be empty", nameof(configId));

        if (limit <= 0 || limit > 1000)
            throw new ArgumentException("Limit must be between 1 and 1000", nameof(limit));

        try {
            return await _auditRepository.GetAuditHistoryAsync(configId, limit);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving audit history for configuration {ConfigId}", configId);
            throw;
        }
    }

    /// <summary>
    /// Get audit summary statistics.
    /// </summary>
    public async Task<ToolConfigurationAuditSummary> GetAuditSummaryAsync() {
        try {
            return await _auditRepository.GetAuditSummaryAsync();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving audit summary");
            throw;
        }
    }

    /// <summary>
    /// Get audit entries filtered by action type.
    /// </summary>
    public async Task<ToolConfigurationAuditEntry[]> GetAuditEntriesByActionAsync(string action) {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action must not be empty", nameof(action));

        try {
            return await _auditRepository.GetAuditEntriesByActionAsync(action);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving audit entries for action {Action}", action);
            throw;
        }
    }
}
