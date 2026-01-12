using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Application.Services.Mcp;

/// <summary>
/// Manages MCP tool configuration with caching and refresh
/// </summary>
public class McpToolManager
{
    private readonly IToolConfigurationService _configProvider;
    private readonly ILogger<McpToolManager> _logger;
    private readonly TimeSpan _refreshInterval;
    private McpToolConfiguration[]? _cachedTools;
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public McpToolManager(
        IToolConfigurationService configProvider,
        ILogger<McpToolManager> logger)
        : this(configProvider, TimeSpan.FromMinutes(5), logger)
    {
    }

    public McpToolManager(
        IToolConfigurationService configProvider,
        TimeSpan refreshInterval,
        ILogger<McpToolManager> logger)
    {
        ArgumentNullException.ThrowIfNull(configProvider);
        ArgumentNullException.ThrowIfNull(logger);
        if (refreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshInterval), refreshInterval, "refreshInterval must be greater than zero");
        }

        _configProvider = configProvider;
        _refreshInterval = refreshInterval;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await RefreshToolsAsync();
            _logger.LogInformation("MCP tools initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize MCP tools");
        }
    }

    public async Task<McpToolConfiguration[]> GetEnabledToolsAsync()
    {
        var now = DateTime.UtcNow;

        if (_lastRefresh != DateTime.MinValue && (now - _lastRefresh) < _refreshInterval)
        {
            return _cachedTools ?? Array.Empty<McpToolConfiguration>();
        }

        await RefreshToolsAsync();
        return _cachedTools ?? Array.Empty<McpToolConfiguration>();
    }

    private async Task RefreshToolsAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            // Double-check after lock
            var now = DateTime.UtcNow;
            if (_lastRefresh != DateTime.MinValue && (now - _lastRefresh) < _refreshInterval)
            {
                return;
            }

            _cachedTools = await _configProvider.GetEnabledToolsAsync();
            _lastRefresh = now;
            _logger.LogDebug("Refreshed {Count} MCP tools", _cachedTools.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh MCP tools");
            _cachedTools ??= Array.Empty<McpToolConfiguration>();
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}