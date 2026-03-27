using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin.ViewModels;

internal partial class LandingViewModel : ObservableObject
{
    private readonly IAdminAuthService _authService;
    private readonly IConfigurationStateService _configurationStateService;
    private readonly IAppFlowCoordinator _appFlowCoordinator;
    private readonly ILogger<LandingViewModel> _logger;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isAuthConfigured;

    [ObservableProperty]
    private bool _isApiConfigured;

    [ObservableProperty]
    private bool _isSignedIn;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public LandingViewModel(
        IAdminAuthService authService,
        IConfigurationStateService configurationStateService,
        IAppFlowCoordinator appFlowCoordinator,
        ILogger<LandingViewModel> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configurationStateService = configurationStateService ?? throw new ArgumentNullException(nameof(configurationStateService));
        _appFlowCoordinator = appFlowCoordinator ?? throw new ArgumentNullException(nameof(appFlowCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string PrimaryActionText => IsBusy
        ? "Signing In..."
        : IsSignedIn
            ? "Continue To Admin"
            : "Sign In";

    public string ConfigurationSummary => IsAuthConfigured && IsApiConfigured
        ? "Saved API and authentication settings are ready."
        : "Open Settings to configure the API, sign-in, and local processor options on this machine.";

    public string SessionSummary => IsSignedIn
        ? "A cached sign-in was found for this device."
        : "Sign in to open the admin workspace.";

    public async Task InitializeAsync()
    {
        await _configurationStateService.LoadConfigurationAsync().ConfigureAwait(false);
        RefreshState();
    }

    partial void OnIsBusyChanged(bool value)
    {
        SignInCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PrimaryActionText));
    }

    partial void OnIsSignedInChanged(bool value)
    {
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(SessionSummary));
    }

    partial void OnIsAuthConfiguredChanged(bool value) =>
        OnPropertyChanged(nameof(ConfigurationSummary));

    partial void OnIsApiConfiguredChanged(bool value) =>
        OnPropertyChanged(nameof(ConfigurationSummary));

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        await InitializeAsync().ConfigureAwait(false);

        if (!IsAuthConfigured)
        {
            StatusMessage = "Authentication is not configured yet. Open Settings first.";
            return;
        }

        IsBusy = true;
        StatusMessage = IsSignedIn
            ? "Opening the admin workspace..."
            : "Starting sign-in...";

        try
        {
            var signedIn = IsSignedIn || await _authService.SignInAsync().ConfigureAwait(false);
            RefreshState();

            if (!signedIn)
            {
                StatusMessage = "Sign-in did not complete. Check your saved settings and try again.";
                return;
            }

            StatusMessage = "Sign-in complete.";
            await _appFlowCoordinator.ShowShellAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Landing-page sign-in failed.");
            StatusMessage = "Sign-in failed. Check your saved settings and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSignIn() => !IsBusy;

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        await _appFlowCoordinator.OpenSettingsAsync().ConfigureAwait(false);
        await InitializeAsync().ConfigureAwait(false);
    }

    private void RefreshState()
    {
        IsAuthConfigured = _configurationStateService.IsAuthConfigured;
        IsApiConfigured = _configurationStateService.IsApiConfigured;
        IsSignedIn = _authService.IsSignedIn();

        if (string.IsNullOrWhiteSpace(StatusMessage))
        {
            StatusMessage = IsSignedIn
                ? "Continue into the admin workspace when you are ready."
                : "Use Sign In to open the admin workspace.";
        }
    }
}
