using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Utilities;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for the Settings page.
/// Manages API, authentication, and local processing configuration.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by MAUI framework via DI")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "For data binding")]
internal partial class SettingsViewModel : ObservableObject {
    private static readonly string[] OnnxFileExtensionsWinUI = { ".onnx" };
    private static readonly string[] OnnxFileExtensionsMacOS = { "onnx" };

    private readonly IConfigurationStateService _configService;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0052:Remove unread private members", Justification = "Reserved for future use")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S4487:Unread private field", Justification = "Reserved for future use")]
    private readonly INavigationService _navigationService;
    private readonly ILogger<SettingsViewModel> _logger;

    // ========== API Configuration ==========

    [ObservableProperty]
    private string _apiBaseUrl = string.Empty;

    [ObservableProperty]
    private bool _isApiValid;

    [ObservableProperty]
    private string _apiValidationMessage = string.Empty;

    [ObservableProperty]
    private bool _isTestingApi;

    [ObservableProperty]
    private string _apiTestResult = string.Empty;

    // ========== Authentication Configuration ==========

    [ObservableProperty]
    private string _authClientId = string.Empty;

    [ObservableProperty]
    private string _authAuthority = string.Empty;

    [ObservableProperty]
    private string _authScope = string.Empty;

    [ObservableProperty]
    private bool _isAuthValid;

    [ObservableProperty]
    private string _authValidationMessage = string.Empty;

    // ========== Local Processing Configuration ==========

    [ObservableProperty]
    private string _embeddingModelPath = string.Empty;

    // ========== General State ==========

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    public SettingsViewModel(
        IConfigurationStateService configService,
        INavigationService navigationService,
        ILogger<SettingsViewModel> logger) {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Load current configuration
        LoadCurrentConfiguration();
    }

    /// <summary>
    /// Loads the current configuration values into the ViewModel properties.
    /// </summary>
    private void LoadCurrentConfiguration() {
        ApiBaseUrl = _configService.ApiBaseUrl?.ToString() ?? string.Empty;
        AuthClientId = _configService.AuthClientId ?? string.Empty;
        AuthAuthority = _configService.AuthAuthority ?? string.Empty;
        AuthScope = _configService.AuthScope ?? string.Empty;
        EmbeddingModelPath = _configService.EmbeddingModelPath ?? string.Empty;

        ValidateApiUrl();
        ValidateAuthSettings();
        HasUnsavedChanges = false;
    }

    // ========== Property Change Handlers ==========

    partial void OnApiBaseUrlChanged(string value) {
        ValidateApiUrl();
        HasUnsavedChanges = true;
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }

    partial void OnAuthClientIdChanged(string value) => OnAuthSettingChanged();

    partial void OnAuthAuthorityChanged(string value) => OnAuthSettingChanged();

    partial void OnAuthScopeChanged(string value) => OnAuthSettingChanged();

    /// <summary>
    /// Common handler for auth setting changes.
    /// </summary>
    private void OnAuthSettingChanged() {
        ValidateAuthSettings();
        HasUnsavedChanges = true;
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }

    partial void OnEmbeddingModelPathChanged(string value) {
        HasUnsavedChanges = true;
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }

    // ========== Validation ==========

    private void ValidateApiUrl() {
        if (string.IsNullOrWhiteSpace(ApiBaseUrl)) {
            IsApiValid = false;
            ApiValidationMessage = "API URL is required";
            return;
        }

        if (!Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var uri)) {
            IsApiValid = false;
            ApiValidationMessage = "Invalid URL format";
            return;
        }

        // Allow HTTP only for localhost
        var isLocalhost = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                          uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);

        if (!isLocalhost && !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) {
            IsApiValid = false;
            ApiValidationMessage = "HTTPS required for non-localhost URLs";
            return;
        }

        IsApiValid = true;
        ApiValidationMessage = "Valid";
    }

    private void ValidateAuthSettings() {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(AuthClientId)) {
            issues.Add("Client ID required");
        }

        if (string.IsNullOrWhiteSpace(AuthAuthority)) {
            issues.Add("Authority required");
        }
        else if (!Uri.TryCreate(AuthAuthority, UriKind.Absolute, out _)) {
            issues.Add("Invalid authority URL");
        }

        if (string.IsNullOrWhiteSpace(AuthScope)) {
            issues.Add("Scope required");
        }

        if (issues.Count > 0) {
            IsAuthValid = false;
            AuthValidationMessage = string.Join(", ", issues);
        }
        else {
            IsAuthValid = true;
            AuthValidationMessage = "Valid";
        }
    }

    // ========== Commands ==========

    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    private async Task SaveSettingsAsync() {
        // Validate all settings before saving
        var hasValidationErrors = false;
        var errorMessages = new List<string>();

        if (!IsApiValid) {
            hasValidationErrors = true;
            errorMessages.Add($"API: {ApiValidationMessage}");
        }

        if (!IsAuthValid) {
            hasValidationErrors = true;
            errorMessages.Add($"Auth: {AuthValidationMessage}");
        }

        if (hasValidationErrors) {
            StatusMessage = "Please fix validation errors before saving.";
            var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
            if (window?.Page != null) {
                await window.Page.DisplayAlertAsync(
                    "Validation Error",
                    $"Please fix the following errors:\n\n{string.Join("\n", errorMessages)}",
                    "OK");
            }
            return;
        }

        IsSaving = true;
        StatusMessage = "Saving settings...";

        try {
            // Save API configuration - convert string to Uri
            Uri? apiUri = string.IsNullOrWhiteSpace(ApiBaseUrl) ? null : new Uri(ApiBaseUrl);
            await _configService.SaveApiBaseUrlAsync(apiUri).ConfigureAwait(false);

            // Save auth configuration
            await _configService.SaveAuthConfigurationAsync(
                AuthClientId,
                AuthAuthority,
                AuthScope).ConfigureAwait(false);

            // Save embedding model path
            if (!string.IsNullOrWhiteSpace(EmbeddingModelPath)) {
                await _configService.SaveEmbeddingModelPathAsync(EmbeddingModelPath).ConfigureAwait(false);
            }

            HasUnsavedChanges = false;
            StatusMessage = "Settings saved successfully. Restart the app for changes to take effect.";
            _logger.LogInformation("Settings saved successfully");
            
            // Show success alert to ensure user knows it worked
             var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
            if (window?.Page != null) {
                await window.Page.DisplayAlertAsync("Success", "Settings saved successfully. Please restart the app.", "OK");
            }
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            StatusMessage = $"Failed to save settings: {sanitizedMessage}";
            _logger.LogError(ex, "Failed to save settings");
            
            var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
            if (window?.Page != null) {
                await window.Page.DisplayAlertAsync("Error", $"Failed to save settings: {sanitizedMessage}", "OK");
            }
        }
        finally {
            IsSaving = false;
        }
    }

    private bool CanSaveSettings() => HasUnsavedChanges && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanTestApi))]
    private async Task TestApiConnectionAsync() {
        if (!IsApiValid) {
            ApiTestResult = "Please enter a valid API URL first";
            return;
        }

        IsTestingApi = true;
        ApiTestResult = "Testing connection...";

        try {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var healthUrl = new Uri(ApiBaseUrl.TrimEnd('/') + "/health");

            var response = await httpClient.GetAsync(healthUrl).ConfigureAwait(false);

            if (response.IsSuccessStatusCode) {
                ApiTestResult = "Connection successful!";
                _logger.LogInformation("API connection test successful");
            }
            else {
                ApiTestResult = $"Connection failed: {response.StatusCode}";
                _logger.LogWarning("API connection test failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (HttpRequestException ex) {
            ApiTestResult = $"Connection failed: {ex.Message}";
            _logger.LogWarning(ex, "API connection test failed");
        }
        catch (TaskCanceledException ex) {
            ApiTestResult = "Connection timed out";
            _logger.LogWarning(ex, "API connection test timed out");
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            ApiTestResult = $"Error: {sanitizedMessage}";
            _logger.LogError(ex, "API connection test error");
        }
        finally {
            IsTestingApi = false;
        }
    }

    private bool CanTestApi() => IsApiValid && !IsTestingApi;

    [RelayCommand]
    private async Task BrowseModelPathAsync() {
        try {
            var result = await FilePicker.PickAsync(new PickOptions {
                PickerTitle = "Select ONNX Embedding Model",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, OnnxFileExtensionsWinUI },
                    { DevicePlatform.macOS, OnnxFileExtensionsMacOS }
                })
            }).ConfigureAwait(false);

            if (result != null) {
                EmbeddingModelPath = result.FullPath;
                _logger.LogInformation("Embedding model path selected: {Path}", result.FileName);
            }
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            StatusMessage = $"Failed to select file: {sanitizedMessage}";
            _logger.LogError(ex, "Failed to browse for model file");
        }
    }

    [RelayCommand]
    private async Task ResetSettingsAsync() {
        var confirmed = await ShowConfirmationAsync(
            "Reset Settings",
            "Are you sure you want to clear all settings? This action cannot be undone.").ConfigureAwait(false);

        if (!confirmed) {
            return;
        }

        try {
            await _configService.ClearConfigurationAsync().ConfigureAwait(false);

            // Reset local properties
            ApiBaseUrl = string.Empty;
            AuthClientId = string.Empty;
            AuthAuthority = string.Empty;
            AuthScope = string.Empty;
            EmbeddingModelPath = string.Empty;

            HasUnsavedChanges = false;
            StatusMessage = "Settings cleared. Restart the app for changes to take effect.";
            _logger.LogInformation("Settings cleared");
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            StatusMessage = $"Failed to clear settings: {sanitizedMessage}";
            _logger.LogError(ex, "Failed to clear settings");
        }
    }

    private static async Task<bool> ShowConfirmationAsync(string title, string message) {
        var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
        if (window?.Page != null) {
            return await window.Page.DisplayAlertAsync(title, message, "Yes", "No").ConfigureAwait(false);
        }
        return false;
    }
}
