using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Services.Dtos;
using MotorcycleRAG.Admin.Utilities;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for managing MCP tool configurations in the admin panel.
/// Handles loading, saving, and managing MCP tool enable/disable states with validation.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "For data binding")]
internal partial class ToolsViewModel : ObservableObject
{
    private readonly ApiClient _apiClient;
    private readonly IAdminAuthService _authService;
    private readonly IConfigurationStateService _configService;
    private readonly ILogger<ToolsViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<ToolConfigItem> tools = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private ToolConfigItem? selectedTool;

    public ToolsViewModel(
        ApiClient apiClient,
        IAdminAuthService authService,
        IConfigurationStateService configService,
        ILogger<ToolsViewModel> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [RelayCommand]
    internal async Task RefreshAsync()
    {
        await MauiThreading.RunOnMainThreadAsync(() =>
        {
            IsLoading = true;
            ErrorMessage = null;
        }).ConfigureAwait(false);

        try
        {
            var toolConfigs = await MauiThreading.RunOffMainThreadAsync(async () =>
            {
                var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
                if (!authorized)
                {
                    return null;
                }

                return await _apiClient.GetMcpToolsAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);

            if (toolConfigs is null)
            {
                return;
            }

            await MauiThreading.RunOnMainThreadAsync(() =>
            {
                Tools.Clear();
                foreach (var config in toolConfigs)
                {
                    Tools.Add(new ToolConfigItem(config));
                }
            }).ConfigureAwait(false);

            _logger.LogInformation("Loaded {Count} MCP tool configurations", toolConfigs.Length);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "API not available for loading MCP tools");
            await MauiThreading.RunOnMainThreadAsync(() => Tools.Clear()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            _logger.LogError(ex, "Error loading MCP tools");

            await MauiThreading.RunOnMainThreadAsync(() =>
                ErrorMessage = $"Failed to load MCP tools: {sanitizedMessage}").ConfigureAwait(false);
            await ErrorPresenter.ShowErrorAsync("Error", ErrorMessage ?? "Failed to load MCP tools.").ConfigureAwait(false);
        }
        finally
        {
            await MauiThreading.RunOnMainThreadAsync(() => IsLoading = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    internal async Task SaveToolAsync(ToolConfigItem? tool)
    {
        if (tool is null)
        {
            return;
        }

        if (!ValidateTool(tool))
        {
            await MauiThreading.RunOnMainThreadAsync(() =>
                ErrorMessage = "Invalid tool configuration. Please check the settings.").ConfigureAwait(false);
            return;
        }

        await MauiThreading.RunOnMainThreadAsync(() =>
        {
            IsLoading = true;
            ErrorMessage = null;
        }).ConfigureAwait(false);

        try
        {
            var saved = await MauiThreading.RunOffMainThreadAsync(async () =>
            {
                var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
                if (!authorized)
                {
                    return false;
                }

                var updateRequest = new UpdateMcpToolRequest
                {
                    IsEnabled = tool.IsEnabled,
                    ChangeReason = tool.IsEnabled ? "Enabled via admin panel" : "Disabled via admin panel"
                };

                await _apiClient.UpdateMcpToolAsync(tool.ToolId, updateRequest).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);

            if (!saved)
            {
                return;
            }

            _logger.LogInformation(
                "Saved MCP tool configuration '{ToolId}': IsEnabled={IsEnabled}",
                tool.ToolId,
                tool.IsEnabled);

            await ErrorPresenter.ShowSuccessAsync(
                "Success",
                $"Tool '{tool.Name}' has been {(tool.IsEnabled ? "enabled" : "disabled")}.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            _logger.LogError(ex, "Error saving MCP tool '{ToolId}'", tool.ToolId);

            await MauiThreading.RunOnMainThreadAsync(() =>
                ErrorMessage = $"Failed to save tool: {sanitizedMessage}").ConfigureAwait(false);
            await ErrorPresenter.ShowErrorAsync("Error", ErrorMessage ?? "Failed to save tool.").ConfigureAwait(false);
        }
        finally
        {
            await MauiThreading.RunOnMainThreadAsync(() => IsLoading = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    internal void SelectTool(ToolConfigItem? tool)
    {
        SelectedTool = tool;
        _logger.LogDebug("Selected tool: {ToolId}", tool?.ToolId ?? "null");
    }

    internal Task InitializeAsync() => RefreshAsync();

    private bool ValidateTool(ToolConfigItem tool)
    {
        if (string.IsNullOrWhiteSpace(tool.ToolId))
        {
            _logger.LogWarning("Tool ID is empty");
            return false;
        }

        if (string.IsNullOrWhiteSpace(tool.Name))
        {
            _logger.LogWarning("Tool name is empty");
            return false;
        }

        if (tool.ServerUrl == null)
        {
            _logger.LogWarning("Tool server URL is empty");
            return false;
        }

        if (!UrlValidator.IsValidUrl(tool.ServerUrl, allowLocalhost: true))
        {
            _logger.LogWarning("Tool server URL is invalid or disallowed (SSRF protection): {ServerUrl}", tool.ServerUrl);
            return false;
        }

        return true;
    }

    private async Task<bool> EnsureAuthorizedAsync()
    {
        if (!_configService.IsApiConfigured)
        {
            _logger.LogWarning("Tools page blocked: API not configured");
            await ErrorPresenter.ShowWarningAsync(
                "Configuration Required",
                "API is not configured. Go to Settings to configure the API base URL.").ConfigureAwait(false);
            return false;
        }

        if (!_authService.IsAuthenticated)
        {
            _logger.LogWarning("Tools page blocked: user not authenticated");
            await ErrorPresenter.ShowWarningAsync(
                "Sign In Required",
                "Please sign in to manage MCP tools.").ConfigureAwait(false);
            return false;
        }

        var isAuthorized = await _authService.IsAuthorizedAdminAsync().ConfigureAwait(false);
        if (!isAuthorized)
        {
            _logger.LogWarning("Tools page blocked: user lacks admin roles");
            await ErrorPresenter.ShowWarningAsync(
                "Access Denied",
                "You do not have permission to manage MCP tools.").ConfigureAwait(false);
            return false;
        }

        return true;
    }
}

/// <summary>
/// View model item for a single MCP tool configuration.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "For data binding")]
internal partial class ToolConfigItem : ObservableObject
{
    internal Guid Id { get; }
    internal string ToolId { get; }
    internal string Name { get; }
    internal string? Description { get; }
    internal Uri ServerUrl { get; }
    internal string ToolType { get; }
    internal string? Version { get; set; }

    [ObservableProperty]
    private bool isEnabled;

    internal ToolConfigItem(McpToolConfigurationDto config)
    {
        Id = config.Id;
        ToolId = config.ToolId;
        Name = config.Name;
        Description = config.Description;
        ServerUrl = config.ServerUrl;
        ToolType = config.ToolType;
        Version = config.Version;
        isEnabled = config.IsEnabled;
    }
}
