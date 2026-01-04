using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Default implementation of MCP Configuration Provider.
/// Delegates to IToolConfigurationRepository for persistence.
/// </summary>
public class McpConfigurationProvider : IMcpConfigurationProvider {
    private readonly IToolConfigurationRepository _configRepository;
    private readonly ILogger<McpConfigurationProvider> _logger;
    private DateTime _lastRefreshed;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private bool _disposed = false;

    public DateTime LastRefreshed => _lastRefreshed;

    public McpConfigurationProvider(
        IToolConfigurationRepository configRepository,
        ILogger<McpConfigurationProvider> logger) {
        _configRepository = configRepository ?? throw new ArgumentNullException(nameof(configRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lastRefreshed = DateTime.UtcNow;
    }

    /// <summary>
    /// Get all enabled MCP tool configurations
    /// </summary>
    public async Task<McpToolConfiguration[]> GetEnabledToolsAsync() {
        try {
            return await _configRepository.GetEnabledAsync();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving enabled MCP tools");
            throw;
        }
    }

    /// <summary>
    /// Get configuration by tool ID
    /// </summary>
    public async Task<McpToolConfiguration?> GetToolConfigurationAsync(string toolId) {
        if (string.IsNullOrWhiteSpace(toolId))
            return null;

        try {
            var config = await _configRepository.GetByToolIdAsync(toolId);

            if (config != null && config.IsEnabled) {
                return config;
            }

            return null;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving tool configuration for {ToolId}", toolId);
            throw;
        }
    }

    /// <summary>
    /// Get configurations by type
    /// </summary>
    public async Task<McpToolConfiguration[]> GetToolsByTypeAsync(string toolType) {
        if (string.IsNullOrWhiteSpace(toolType))
            return Array.Empty<McpToolConfiguration>();

        try {
            return await _configRepository.GetByTypeAsync(toolType);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving tools by type {ToolType}", toolType);
            throw;
        }
    }

    /// <summary>
    /// Refresh configurations from store with thread-safe locking
    /// </summary>
    public async Task RefreshAsync() {
        await _refreshLock.WaitAsync();
        try {
            _lastRefreshed = DateTime.UtcNow;
            _logger.LogDebug("MCP Configuration provider refreshed at {Time}", _lastRefreshed);
        }
        finally {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Validate that a tool configuration is accessible
    /// </summary>
    public async Task<bool> ValidateToolAsync(McpToolConfiguration tool) {
        if (tool == null)
            return false;

        if (!tool.IsEnabled) {
            _logger.LogWarning("Tool {ToolId} is disabled: {Reason}", tool.ToolId, tool.DisabledReason);
            return false;
        }

        if (tool.ServerUrl == null) {
            _logger.LogWarning("Tool {ToolId} has no server URL configured", tool.ToolId);
            return false;
        }

        try {
            // Validate URL format
            var uri = new Uri(tool.ServerUrl ?? string.Empty);

            // Check timeout configuration
            if (tool.TimeoutMs.HasValue && tool.TimeoutMs <= 0) {
                _logger.LogWarning("Tool {ToolId} has invalid timeout configuration: {Timeout}ms",
                    tool.ToolId, tool.TimeoutMs);
                return false;
            }

            _logger.LogDebug("Tool {ToolId} validation passed", tool.ToolId);
            return await Task.FromResult(true);
        }
        catch (UriFormatException ex) {
            _logger.LogError(ex, "Tool {ToolId} has invalid server URL: {ServerUrl}",
                tool.ToolId, tool.ServerUrl);
            return false;
        }
    }

    /// <summary>
    /// Dispose resources and cleanup
    /// </summary>
    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose implementation
    /// </summary>
    protected virtual void Dispose(bool disposing) {
        if (_disposed)
            return;

        if (disposing) {
            // Dispose semaphore
            _refreshLock?.Dispose();
        }

        _disposed = true;
    }

    /// <summary>
    /// Finalizer for resource cleanup
    /// </summary>
    ~McpConfigurationProvider() {
        Dispose(false);
    }
}
