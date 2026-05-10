using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Services.Logging;
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
    private readonly IConfigurationStateService _configService;
    private readonly ILocalProcessorService _localProcessorService;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0052:Remove unread private members", Justification = "Reserved for future use")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S4487:Unread private field", Justification = "Reserved for future use")]
    private readonly INavigationService _navigationService;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly FileLoggerOptions _fileLoggerOptions;
    private int _localProcessorValidationVersion;
    private bool _isHydratingConfiguration;

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
    private string _embeddingProviderEndpoint = string.Empty;

    [ObservableProperty]
    private string _embeddingModel = string.Empty;

    [ObservableProperty]
    private string _embeddingProvider = string.Empty;

    [ObservableProperty]
    private string _embeddingModelsStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoadingEmbeddingModels;

    [ObservableProperty]
    private string _localProcessorEndpoint = string.Empty;

    [ObservableProperty]
    private string _localProcessorWorkingDirectory = string.Empty;

    [ObservableProperty]
    private string _localProcessorStartCommand = string.Empty;

    [ObservableProperty]
    private int _pdfChunkerMaxTokens = LocalProcessorDefaults.DefaultPdfChunkerMaxTokens;

    [ObservableProperty]
    private int _csvChunkMaxTokens = LocalProcessorDefaults.DefaultCsvChunkMaxTokens;

    [ObservableProperty]
    private string _pdfChunkerTokenizer = LocalProcessorDefaults.DefaultPdfChunkerTokenizer;

    [ObservableProperty]
    private string _localProcessorUploadJobSecret = string.Empty;

    [ObservableProperty]
    private bool _isLocalProcessorValid;

    [ObservableProperty]
    private string _localProcessorValidationMessage = string.Empty;

    // ========== General State ==========

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    // ========== Diagnostics ==========

    [ObservableProperty]
    private string _logFolderPath = string.Empty;

    public ObservableCollection<string> EmbeddingModels { get; } = [];

    public SettingsViewModel(
        IConfigurationStateService configService,
        ILocalProcessorService localProcessorService,
        INavigationService navigationService,
        FileLoggerOptions fileLoggerOptions,
        ILogger<SettingsViewModel> logger) {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _localProcessorService = localProcessorService ?? throw new ArgumentNullException(nameof(localProcessorService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _fileLoggerOptions = fileLoggerOptions ?? throw new ArgumentNullException(nameof(fileLoggerOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        LogFolderPath = _fileLoggerOptions.LogDirectory;

        // Load current configuration
        LoadCurrentConfiguration();
    }

    /// <summary>
    /// Loads the current configuration values into the ViewModel properties.
    /// </summary>
    private void LoadCurrentConfiguration() {
        _isHydratingConfiguration = true;
        try {
            ApiBaseUrl = _configService.ApiBaseUrl?.ToString() ?? string.Empty;
            AuthClientId = _configService.AuthClientId ?? string.Empty;
            AuthAuthority = _configService.AuthAuthority ?? string.Empty;
            AuthScope = _configService.AuthScope ?? string.Empty;
            EmbeddingProviderEndpoint = _configService.EmbeddingProviderEndpoint ?? string.Empty;
            EmbeddingModel = _configService.EmbeddingModel ?? string.Empty;
            EmbeddingProvider = string.Empty;
            EmbeddingModelsStatusMessage = string.Empty;
            SetEmbeddingModels(string.IsNullOrWhiteSpace(EmbeddingModel)
                ? []
                : [EmbeddingModel]);
            LocalProcessorEndpoint = _configService.LocalProcessorEndpoint?.ToString() ?? LocalProcessorDefaults.DefaultEndpoint;
            LocalProcessorWorkingDirectory = _configService.LocalProcessorWorkingDirectory ?? string.Empty;
            LocalProcessorStartCommand = _configService.LocalProcessorStartCommand ?? LocalProcessorDefaults.DefaultStartCommand;
            PdfChunkerMaxTokens = _configService.PdfChunkerMaxTokens > 0
                ? _configService.PdfChunkerMaxTokens
                : LocalProcessorDefaults.DefaultPdfChunkerMaxTokens;
            CsvChunkMaxTokens = _configService.CsvChunkMaxTokens > 0
                ? _configService.CsvChunkMaxTokens
                : LocalProcessorDefaults.DefaultCsvChunkMaxTokens;
            PdfChunkerTokenizer = _configService.PdfChunkerTokenizer ?? LocalProcessorDefaults.DefaultPdfChunkerTokenizer;
            LocalProcessorUploadJobSecret = _configService.LocalProcessorUploadJobSecret ?? string.Empty;
        }
        finally {
            _isHydratingConfiguration = false;
        }

        ValidateApiUrl();
        ValidateAuthSettings();
        QueueLocalProcessorValidation();
        LoadEmbeddingModelsCommand.NotifyCanExecuteChanged();
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

    partial void OnEmbeddingProviderEndpointChanged(string value) {
        if (!_isHydratingConfiguration) {
            EmbeddingProvider = string.Empty;
            EmbeddingModelsStatusMessage = string.Empty;
            SetEmbeddingModels([]);
            EmbeddingModel = string.Empty;
            HasUnsavedChanges = true;
        }

        LoadEmbeddingModelsCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }

    partial void OnEmbeddingModelChanged(string value) {
        if (_isHydratingConfiguration) {
            return;
        }

        HasUnsavedChanges = true;
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }

    partial void OnLocalProcessorEndpointChanged(string value) => OnLocalProcessorSettingChanged();

    partial void OnLocalProcessorWorkingDirectoryChanged(string value) => OnLocalProcessorSettingChanged();

    partial void OnLocalProcessorStartCommandChanged(string value) => OnLocalProcessorSettingChanged();

    partial void OnLocalProcessorUploadJobSecretChanged(string value) => OnLocalProcessorSettingChanged();

    partial void OnPdfChunkerMaxTokensChanged(int value) => OnLocalProcessorSettingChanged();

    partial void OnCsvChunkMaxTokensChanged(int value) => OnLocalProcessorSettingChanged();

    partial void OnPdfChunkerTokenizerChanged(string value) => OnLocalProcessorSettingChanged();

    private void OnLocalProcessorSettingChanged() {
        QueueLocalProcessorValidation();
        HasUnsavedChanges = true;
        LoadEmbeddingModelsCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingEmbeddingModelsChanged(bool value) => LoadEmbeddingModelsCommand.NotifyCanExecuteChanged();

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
        var isLocalhost = uri.IsLoopback;

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
        else if (AuthScope.EndsWith("/.default", StringComparison.OrdinalIgnoreCase)) {
            issues.Add("Use an explicit admin scope, for example api://<api-client-id>/admin, not /.default");
        }
        else if (AuthScope.EndsWith("/admin_access", StringComparison.OrdinalIgnoreCase) ||
                 AuthScope.EndsWith("/access_as_user", StringComparison.OrdinalIgnoreCase)) {
            issues.Add("Legacy scope suffixes are not supported. Use api://<api-client-id>/admin");
        }
        else if (!AdminAuthConfigurationHelper.IsSupportedScope(AuthScope)) {
            issues.Add("Scope must be a full URI, for example api://<api-client-id>/admin");
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

    private void QueueLocalProcessorValidation() {
        var validationVersion = Interlocked.Increment(ref _localProcessorValidationVersion);
        _ = ValidateLocalProcessorSettingsAsync(validationVersion)
            .ContinueWith(
                t => _logger.LogError(t.Exception, "Local processor validation failed unexpectedly"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }

    private async Task ValidateLocalProcessorSettingsAsync(int validationVersion) {
        var validationResult = await MauiThreading.RunOffMainThreadAsync(
            () => BuildLocalProcessorValidation(
                LocalProcessorEndpoint,
                LocalProcessorWorkingDirectory,
                LocalProcessorStartCommand,
                PdfChunkerMaxTokens,
                CsvChunkMaxTokens,
                PdfChunkerTokenizer)).ConfigureAwait(false);

        await MauiThreading.RunOnMainThreadAsync(() => {
            if (validationVersion != _localProcessorValidationVersion) {
                return;
            }

            IsLocalProcessorValid = validationResult.IsValid;
            LocalProcessorValidationMessage = validationResult.Message;
        }).ConfigureAwait(false);
    }

    private static (bool IsValid, string Message) BuildLocalProcessorValidation(
        string endpointValue,
        string workingDirectoryValue,
        string startCommandValue,
        int pdfChunkerMaxTokens,
        int csvChunkMaxTokens,
        string pdfChunkerTokenizerValue) {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(endpointValue)) {
            issues.Add("Endpoint required");
        }
        else if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpointUri)) {
            issues.Add("Invalid endpoint URL");
        }
        else if (!endpointUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                 !endpointUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) {
            issues.Add("Endpoint must use HTTP or HTTPS");
        }

        if (string.IsNullOrWhiteSpace(workingDirectoryValue)) {
            issues.Add("Working directory required");
        }
        else if (!Directory.Exists(workingDirectoryValue)) {
            issues.Add("Working directory not found");
        }
        else if (!File.Exists(Path.Combine(workingDirectoryValue, "src", "main.py"))) {
            issues.Add("src/main.py not found in working directory");
        }

        if (string.IsNullOrWhiteSpace(startCommandValue)) {
            issues.Add("Start command required");
        }

        if (pdfChunkerMaxTokens <= 0) {
            issues.Add("PDF chunk size must be a positive integer");
        }

        if (csvChunkMaxTokens <= 0) {
            issues.Add("CSV chunk size must be a positive integer");
        }

        if (string.IsNullOrWhiteSpace(pdfChunkerTokenizerValue)) {
            issues.Add("PDF tokenizer is required");
        }

        if (issues.Count > 0) {
            return (false, string.Join(", ", issues));
        }

        return (true, "Valid");
    }

    private void SetEmbeddingModels(IEnumerable<string> models) {
        EmbeddingModels.Clear();
        foreach (var model in models.Where(model => !string.IsNullOrWhiteSpace(model))) {
            EmbeddingModels.Add(model);
        }
    }

    // ========== Commands ==========

    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    private async Task SaveSettingsAsync() {
        ValidateApiUrl();
        ValidateAuthSettings();
        await ValidateLocalProcessorSettingsAsync(Interlocked.Increment(ref _localProcessorValidationVersion)).ConfigureAwait(false);

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

        if (!IsLocalProcessorValid) {
            hasValidationErrors = true;
            errorMessages.Add($"Local Processor: {LocalProcessorValidationMessage}");
        }

        if (hasValidationErrors) {
            await MauiThreading.RunOnMainThreadAsync(() =>
                StatusMessage = "Please fix validation errors before saving.").ConfigureAwait(false);
            await ErrorPresenter.ShowErrorAsync(
                "Validation Error",
                $"Please fix the following errors:\n\n{string.Join("\n", errorMessages)}").ConfigureAwait(false);
            return;
        }

        await MauiThreading.RunOnMainThreadAsync(() => {
            IsSaving = true;
            StatusMessage = "Saving settings...";
        }).ConfigureAwait(false);

        try {
            var apiBaseUrl = ApiBaseUrl;
            var authClientId = AuthClientId;
            var authAuthority = AuthAuthority;
            var authScope = AuthScope;
            var embeddingProviderEndpoint = EmbeddingProviderEndpoint;
            var embeddingModel = EmbeddingModel;
            var localProcessorEndpoint = LocalProcessorEndpoint;
            var localProcessorWorkingDirectory = LocalProcessorWorkingDirectory;
            var localProcessorStartCommand = LocalProcessorStartCommand;
            var localProcessorUploadJobSecret = LocalProcessorUploadJobSecret;
            var pdfChunkerMaxTokens = PdfChunkerMaxTokens;
            var csvChunkMaxTokens = CsvChunkMaxTokens;
            var pdfChunkerTokenizer = PdfChunkerTokenizer;

            await MauiThreading.RunOffMainThreadAsync(async () => {
                Uri? apiUri = string.IsNullOrWhiteSpace(apiBaseUrl) ? null : new Uri(apiBaseUrl);
                await _configService.SaveApiBaseUrlAsync(apiUri).ConfigureAwait(false);
                await _configService.SaveAuthConfigurationAsync(
                    authClientId,
                    authAuthority,
                    authScope).ConfigureAwait(false);
                await _configService.SaveEmbeddingConfigurationAsync(embeddingProviderEndpoint, embeddingModel).ConfigureAwait(false);

                await _configService.SaveLocalProcessorConfigurationAsync(
                    string.IsNullOrWhiteSpace(localProcessorEndpoint) ? null : new Uri(localProcessorEndpoint),
                    localProcessorWorkingDirectory,
                    localProcessorStartCommand,
                    localProcessorUploadJobSecret,
                    pdfChunkerMaxTokens,
                    csvChunkMaxTokens,
                    pdfChunkerTokenizer).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await MauiThreading.RunOnMainThreadAsync(() => {
                HasUnsavedChanges = false;
                StatusMessage = "Settings saved successfully.";
            }).ConfigureAwait(false);
            _logger.LogInformation("Settings saved successfully");

            await ErrorPresenter.ShowSuccessAsync("Success", "Settings saved successfully.").ConfigureAwait(false);
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            await MauiThreading.RunOnMainThreadAsync(() =>
                StatusMessage = $"Failed to save settings: {sanitizedMessage}").ConfigureAwait(false);
            _logger.LogError(ex, "Failed to save settings");

            await ErrorPresenter.ShowErrorAsync("Error", $"Failed to save settings: {sanitizedMessage}").ConfigureAwait(false);
        }
        finally {
            await MauiThreading.RunOnMainThreadAsync(() => IsSaving = false).ConfigureAwait(false);
        }
    }

    private bool CanSaveSettings() => HasUnsavedChanges && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanTestApi))]
    private async Task TestApiConnectionAsync() {
        if (!IsApiValid) {
            await MauiThreading.RunOnMainThreadAsync(() =>
                ApiTestResult = "Please enter a valid API URL first").ConfigureAwait(false);
            return;
        }

        await MauiThreading.RunOnMainThreadAsync(() => {
            IsTestingApi = true;
            ApiTestResult = "Testing connection...";
        }).ConfigureAwait(false);

        try {
            var apiBaseUrl = ApiBaseUrl;
            var testResult = await MauiThreading.RunOffMainThreadAsync(async () => {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var healthUrl = new Uri(apiBaseUrl.TrimEnd('/') + "/health");
                var response = await httpClient.GetAsync(healthUrl).ConfigureAwait(false);
                return response.IsSuccessStatusCode
                    ? "Connection successful!"
                    : $"Connection failed: {response.StatusCode}";
            }).ConfigureAwait(false);

            await MauiThreading.RunOnMainThreadAsync(() => ApiTestResult = testResult).ConfigureAwait(false);
            if (string.Equals(testResult, "Connection successful!", StringComparison.Ordinal)) {
                _logger.LogInformation("API connection test successful");
            }
            else {
                _logger.LogWarning("API connection test failed: {Result}", testResult);
            }
        }
        catch (HttpRequestException ex) {
            await MauiThreading.RunOnMainThreadAsync(() =>
                ApiTestResult = $"Connection failed: {ex.Message}").ConfigureAwait(false);
            _logger.LogWarning(ex, "API connection test failed");
        }
        catch (TaskCanceledException ex) {
            await MauiThreading.RunOnMainThreadAsync(() => ApiTestResult = "Connection timed out").ConfigureAwait(false);
            _logger.LogWarning(ex, "API connection test timed out");
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            await MauiThreading.RunOnMainThreadAsync(() =>
                ApiTestResult = $"Error: {sanitizedMessage}").ConfigureAwait(false);
            _logger.LogError(ex, "API connection test error");
        }
        finally {
            await MauiThreading.RunOnMainThreadAsync(() => IsTestingApi = false).ConfigureAwait(false);
        }
    }

    private bool CanTestApi() => IsApiValid && !IsTestingApi;

    [RelayCommand(CanExecute = nameof(CanLoadEmbeddingModels))]
    private async Task LoadEmbeddingModelsAsync() {
        if (string.IsNullOrWhiteSpace(EmbeddingProviderEndpoint)) {
            await MauiThreading.RunOnMainThreadAsync(() =>
                EmbeddingModelsStatusMessage = "Embedding provider endpoint is required.").ConfigureAwait(false);
            return;
        }

        await MauiThreading.RunOnMainThreadAsync(() => {
            IsLoadingEmbeddingModels = true;
            EmbeddingModelsStatusMessage = "Loading embedding models...";
        }).ConfigureAwait(false);

        try {
            var providerEndpoint = EmbeddingProviderEndpoint;
            var discovery = await MauiThreading.RunOffMainThreadAsync(
                () => _localProcessorService.GetEmbeddingModelsAsync(providerEndpoint)).ConfigureAwait(false);

            await MauiThreading.RunOnMainThreadAsync(() => {
                EmbeddingProvider = discovery.Provider;
                SetEmbeddingModels(discovery.Models);

                if (EmbeddingModels.Count == 0) {
                    EmbeddingModel = string.Empty;
                    EmbeddingModelsStatusMessage = $"No embedding models found for {discovery.Provider}.";
                }
                else {
                    if (string.IsNullOrWhiteSpace(EmbeddingModel) || !EmbeddingModels.Contains(EmbeddingModel)) {
                        EmbeddingModel = EmbeddingModels[0];
                    }

                    EmbeddingModelsStatusMessage = $"Loaded {EmbeddingModels.Count} model(s) from {discovery.Provider}.";
                }
            }).ConfigureAwait(false);

            _logger.LogInformation(
                "Loaded {ModelCount} embedding models from provider {Provider}",
                discovery.Models.Count,
                discovery.Provider);
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            await MauiThreading.RunOnMainThreadAsync(() => {
                EmbeddingProvider = string.Empty;
                SetEmbeddingModels([]);
                EmbeddingModel = string.Empty;
                EmbeddingModelsStatusMessage = $"Failed to load models: {sanitizedMessage}";
            }).ConfigureAwait(false);
            _logger.LogWarning(ex, "Failed to load embedding models from provider endpoint {Endpoint}", EmbeddingProviderEndpoint);
        }
        finally {
            await MauiThreading.RunOnMainThreadAsync(() => IsLoadingEmbeddingModels = false).ConfigureAwait(false);
        }
    }

    private bool CanLoadEmbeddingModels() =>
        !IsLoadingEmbeddingModels
        && !string.IsNullOrWhiteSpace(EmbeddingProviderEndpoint)
        && !string.IsNullOrWhiteSpace(LocalProcessorEndpoint)
        && Uri.TryCreate(LocalProcessorEndpoint, UriKind.Absolute, out _);

    [RelayCommand]
    private async Task ResetSettingsAsync() {
        var confirmed = await ErrorPresenter.ShowConfirmAsync(
            "Reset Settings",
            "Are you sure you want to clear all settings? This action cannot be undone.").ConfigureAwait(false);

        if (!confirmed) {
            return;
        }

        try {
            var resetState = await MauiThreading.RunOffMainThreadAsync(async () => {
                await _configService.ClearConfigurationAsync().ConfigureAwait(false);
                return new {
                    LocalProcessorEndpoint = LocalProcessorDefaults.DefaultEndpoint,
                    LocalProcessorWorkingDirectory = LocalProcessorDefaults.TryFindWorkingDirectory() ?? string.Empty,
                    LocalProcessorStartCommand = LocalProcessorDefaults.DefaultStartCommand
                };
            }).ConfigureAwait(false);

            await MauiThreading.RunOnMainThreadAsync(() => {
                ApiBaseUrl = string.Empty;
                AuthClientId = string.Empty;
                AuthAuthority = string.Empty;
                AuthScope = string.Empty;
                EmbeddingProviderEndpoint = string.Empty;
                EmbeddingModel = string.Empty;
                EmbeddingProvider = string.Empty;
                EmbeddingModelsStatusMessage = string.Empty;
                SetEmbeddingModels([]);
                LocalProcessorEndpoint = resetState.LocalProcessorEndpoint;
                LocalProcessorWorkingDirectory = resetState.LocalProcessorWorkingDirectory;
                LocalProcessorStartCommand = resetState.LocalProcessorStartCommand;
                LocalProcessorUploadJobSecret = string.Empty;
                HasUnsavedChanges = false;
                StatusMessage = "Settings cleared.";
            }).ConfigureAwait(false);
            _logger.LogInformation("Settings cleared");
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            await MauiThreading.RunOnMainThreadAsync(() =>
                StatusMessage = $"Failed to clear settings: {sanitizedMessage}").ConfigureAwait(false);
            _logger.LogError(ex, "Failed to clear settings");
        }
    }

    [RelayCommand]
    private void OpenLogFolder() {
        if (!Directory.Exists(LogFolderPath)) {
            Directory.CreateDirectory(LogFolderPath);
        }
        Process.Start(new ProcessStartInfo("explorer.exe", LogFolderPath) { UseShellExecute = true });
    }
}
