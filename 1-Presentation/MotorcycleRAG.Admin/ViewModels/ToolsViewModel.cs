using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Services.Dtos;
using MotorcycleRAG.Admin.Utilities;
using MotorcycleRAG.Admin.Constants;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for managing MCP tool configurations in the admin panel.
/// Handles loading, saving, and managing MCP tool enable/disable states with validation.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "For data binding")]
internal partial class ToolsViewModel : ObservableObject {
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

    public ToolsViewModel(ApiClient apiClient, IAdminAuthService authService, IConfigurationStateService configService, ILogger<ToolsViewModel> logger) {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Commands

    /// <summary>
    /// Command to load MCP tools from the API
    /// </summary>
    [RelayCommand]
    internal async Task RefreshAsync() {
        IsLoading = true;
        ErrorMessage = null;

        try {
            var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
            if (!authorized) {
                return;
            }
            var toolConfigs = await _apiClient.GetMcpToolsAsync();

            await MainThread.InvokeOnMainThreadAsync(() => {
                Tools.Clear();
                foreach (var config in toolConfigs) {
                    Tools.Add(new ToolConfigItem(config));
                }
            });

            _logger.LogInformation("Loaded {Count} MCP tool configurations", toolConfigs.Length);
        }
        catch (HttpRequestException ex) {
            // API not available - silently fail
            _logger.LogWarning(ex, "API not available for loading MCP tools");
            await MainThread.InvokeOnMainThreadAsync(() => Tools.Clear());
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            _logger.LogError(ex, "Error loading MCP tools");

            await MainThread.InvokeOnMainThreadAsync(async () => {
                ErrorMessage = $"Failed to load MCP tools: {sanitizedMessage}";
                var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
                if (window?.Page != null) {
                    await window.Page.DisplayAlertAsync("Error", ErrorMessage, "OK");
                }
            });
        }
        finally {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Command to save a tool configuration change
    /// </summary>
    [RelayCommand]
    internal async Task SaveToolAsync(ToolConfigItem? tool) {
        if (tool == null)
            return;

        IsLoading = true;
        ErrorMessage = null;

        try {
            // Validate tool configuration
            if (!ValidateTool(tool)) {
                ErrorMessage = "Invalid tool configuration. Please check the settings.";
                return;
            }

            var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
            if (!authorized) {
                return;
            }

            // Call API to update tool
            var updateRequest = new UpdateMcpToolRequest {
                IsEnabled = tool.IsEnabled,
                ChangeReason = tool.IsEnabled ? "Enabled via admin panel" : "Disabled via admin panel"
            };

            await _apiClient.UpdateMcpToolAsync(tool.ToolId, updateRequest);

            _logger.LogInformation("Saved MCP tool configuration '{ToolId}': IsEnabled={IsEnabled}",
                tool.ToolId, tool.IsEnabled);

            // Show success message
            await MainThread.InvokeOnMainThreadAsync(async () => {
                var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
                if (window?.Page != null) {
                    await window.Page.DisplayAlertAsync("Success",
                        $"Tool '{tool.Name}' has been {(tool.IsEnabled ? "enabled" : "disabled")}.", "OK");
                }
            });
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            _logger.LogError(ex, "Error saving MCP tool '{ToolId}'", tool.ToolId);

            await MainThread.InvokeOnMainThreadAsync(async () => {
                ErrorMessage = $"Failed to save tool: {sanitizedMessage}";
                var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
                if (window?.Page != null) {
                    await window.Page.DisplayAlertAsync("Error", ErrorMessage, "OK");
                }
            });
        }
        finally {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Command to select a tool
    /// </summary>
    [RelayCommand]
    internal void SelectTool(ToolConfigItem? tool) {
        SelectedTool = tool;
        _logger.LogDebug("Selected tool: {ToolId}", tool?.ToolId ?? "null");
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Initialize the view model - load MCP tools
    /// </summary>
    internal async Task InitializeAsync() {
        await RefreshAsync();
    }

    #endregion

    #region Validation

    /// <summary>
    /// Validate tool configuration
    /// </summary>
    private bool ValidateTool(ToolConfigItem tool) {
        if (tool == null) {
            _logger.LogWarning("Cannot validate null tool");
            return false;
        }

        // Basic validation
        if (string.IsNullOrWhiteSpace(tool.ToolId)) {
            _logger.LogWarning("Tool ID is empty");
            return false;
        }

        if (string.IsNullOrWhiteSpace(tool.Name)) {
            _logger.LogWarning("Tool name is empty");
            return false;
        }

        if (tool.ServerUrl == null) {
            _logger.LogWarning("Tool server URL is empty");
            return false;
        }

        // Validate URL format with SSRF protection
        if (!UrlValidator.IsValidUrl(tool.ServerUrl)) {
            _logger.LogWarning("Tool server URL is invalid or disallowed (SSRF protection): {ServerUrl}", tool.ServerUrl);
            return false;
        }

        return true;
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Ensure user is authorized to manage MCP tools
    /// </summary>
    private async Task<bool> EnsureAuthorizedAsync() {
        if (!_configService.IsApiConfigured) {
            _logger.LogWarning("Tools page blocked: API not configured");
            await ErrorPresenter.ShowWarningAsync(
                "Configuration Required",
                "API is not configured. Go to Settings to configure the API base URL."
            ).ConfigureAwait(false);
            return false;
        }

        if (!_authService.IsAuthenticated) {
            _logger.LogWarning("Tools page blocked: user not authenticated");
            await ErrorPresenter.ShowWarningAsync(
                "Sign In Required",
                "Please sign in to manage MCP tools."
            ).ConfigureAwait(false);
            return false;
        }

        // Use IsAuthorizedAdminAsync to support Debug mode bypass
        var isAuthorized = await _authService.IsAuthorizedAdminAsync().ConfigureAwait(false);
        if (!isAuthorized) {
            _logger.LogWarning("Tools page blocked: user lacks admin roles");
            await ErrorPresenter.ShowWarningAsync(
                "Access Denied",
                "You do not have permission to manage MCP tools."
            ).ConfigureAwait(false);
            return false;
        }

        return true;
    }

    #endregion
}

/// <summary>
/// View model item for a single MCP tool configuration
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "For data binding")]
internal partial class ToolConfigItem : ObservableObject {
    internal Guid Id { get; }
    internal string ToolId { get; }
    internal string Name { get; }
    internal string? Description { get; }
    internal Uri ServerUrl { get; }
    internal string ToolType { get; }
    internal string? Version { get; set; }

    [ObservableProperty]
    private bool isEnabled;

    internal ToolConfigItem(McpToolConfigurationDto config) {
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


