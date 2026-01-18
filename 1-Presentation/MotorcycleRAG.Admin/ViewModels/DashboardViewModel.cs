using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for the admin dashboard page.
/// Handles navigation commands and displays system overview information.
/// Shows configuration status banner when app is not fully configured.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "For data binding")]
internal partial class DashboardViewModel : ObservableObject {
    private readonly INavigationService _navigationService;
    private readonly IConfigurationStateService _configService;

    /// <summary>
    /// Gets a value indicating whether the app is NOT configured.
    /// When true, shows a warning banner.
    /// </summary>
    [ObservableProperty]
    private bool _isNotConfigured;

    /// <summary>
    /// Gets the configuration status message.
    /// </summary>
    [ObservableProperty]
    private string _configurationStatusMessage = string.Empty;

    /// <summary>Command to navigate to the upload page</summary>
    internal ICommand NavigateToUploadCommand { get; }

    /// <summary>Command to navigate to the jobs page</summary>
    internal ICommand NavigateToJobsCommand { get; }

    /// <summary>Command to navigate to web sources page</summary>
    internal ICommand NavigateToWebSourcesCommand { get; }

    /// <summary>Command to navigate to the tools page</summary>
    internal ICommand NavigateToToolsCommand { get; }

    public DashboardViewModel(
        INavigationService navigationService,
        IConfigurationStateService configService) {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        // Initialize navigation commands
        NavigateToUploadCommand = new Command(async () => await _navigationService.NavigateToAsync("uploadpage"));
        NavigateToJobsCommand = new Command(async () => await _navigationService.NavigateToAsync("jobspage"));
        NavigateToWebSourcesCommand = new Command(async () => await _navigationService.NavigateToAsync("websourcespage"));
        NavigateToToolsCommand = new Command(async () => await _navigationService.NavigateToAsync("toolspage"));

        // Check configuration status
        CheckConfigurationStatus();

        // Listen for configuration changes
        _configService.ConfigurationChanged += OnConfigurationChanged;
    }

    /// <summary>
    /// Checks the current configuration status and updates the UI.
    /// </summary>
    private void CheckConfigurationStatus() {
        IsNotConfigured = !_configService.IsConfigured;

        if (IsNotConfigured) {
            var issues = new List<string>();
            if (!_configService.IsApiConfigured) {
                issues.Add("API URL");
            }
            if (!_configService.IsAuthConfigured) {
                issues.Add("Authentication");
            }

            ConfigurationStatusMessage = issues.Count > 0
                ? $"Not configured: {string.Join(", ", issues)}. Click 'Configure Now' to set up."
                : "Application is not fully configured.";
        }
        else {
            ConfigurationStatusMessage = string.Empty;
        }
    }

    /// <summary>
    /// Handles configuration changes from the ConfigurationStateService.
    /// </summary>
    private void OnConfigurationChanged(object? sender, EventArgs e) {
        // Re-check configuration on the main thread
        MainThread.BeginInvokeOnMainThread(CheckConfigurationStatus);
    }

    [RelayCommand]
    private async Task NavigateToSettingsAsync() {
        await _navigationService.NavigateToAsync("settingspage");
    }
}
