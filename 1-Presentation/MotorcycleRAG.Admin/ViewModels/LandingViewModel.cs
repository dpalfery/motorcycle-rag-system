using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Services.Logging;
using MotorcycleRAG.Admin.Utilities;

namespace MotorcycleRAG.Admin.ViewModels;

internal partial class LandingViewModel : ObservableObject
{
    private readonly IAdminAuthService _authService;
    private readonly IApiWarmupService _apiWarmupService;
    private readonly IConfigurationStateService _configurationStateService;
    private readonly IAppFlowCoordinator _appFlowCoordinator;
    private readonly ILogger<LandingViewModel> _logger;
    private readonly FileLoggerOptions _fileLoggerOptions;

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
        IApiWarmupService apiWarmupService,
        IConfigurationStateService configurationStateService,
        IAppFlowCoordinator appFlowCoordinator,
        FileLoggerOptions fileLoggerOptions,
        ILogger<LandingViewModel> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _apiWarmupService = apiWarmupService ?? throw new ArgumentNullException(nameof(apiWarmupService));
        _configurationStateService = configurationStateService ?? throw new ArgumentNullException(nameof(configurationStateService));
        _appFlowCoordinator = appFlowCoordinator ?? throw new ArgumentNullException(nameof(appFlowCoordinator));
        _fileLoggerOptions = fileLoggerOptions ?? throw new ArgumentNullException(nameof(fileLoggerOptions));
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
        await MauiThreading.RunOffMainThreadAsync(() => _configurationStateService.LoadConfigurationAsync()).ConfigureAwait(false);
        await MauiThreading.RunOnMainThreadAsync(RefreshState).ConfigureAwait(false);
        _ = WarmUpApiAsync();
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
            await MauiThreading.RunOnMainThreadAsync(() =>
                StatusMessage = "Authentication is not configured yet. Open Settings first.").ConfigureAwait(false);
            return;
        }

        await MauiThreading.RunOnMainThreadAsync(() =>
        {
            IsBusy = true;
            StatusMessage = IsSignedIn
                ? "Opening the admin workspace..."
                : GetSignInStatusMessage();
        }).ConfigureAwait(false);

        try
        {
            var signedIn = IsSignedIn
                || await MauiThreading.RunOffMainThreadAsync(() => _authService.SignInAsync()).ConfigureAwait(false);
            await MauiThreading.RunOnMainThreadAsync(RefreshState).ConfigureAwait(false);

            if (!signedIn)
            {
                var authError = _authService.LastAuthErrorMessage;
                var logHint = $" Log file: {_fileLoggerOptions.LogDirectory}";
                await MauiThreading.RunOnMainThreadAsync(() =>
                    StatusMessage = string.IsNullOrWhiteSpace(authError)
                        ? $"Sign-in did not complete. Check your saved settings and try again.{logHint}"
                        : $"Sign-in failed: {authError}{logHint}").ConfigureAwait(false);
                return;
            }

            await MauiThreading.RunOnMainThreadAsync(() => StatusMessage = "Sign-in complete.").ConfigureAwait(false);
            await _appFlowCoordinator.ShowShellAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Landing-page sign-in failed.");
            await MauiThreading.RunOnMainThreadAsync(() =>
                StatusMessage = "Sign-in failed. Check your saved settings and try again.").ConfigureAwait(false);
        }
        finally
        {
            await MauiThreading.RunOnMainThreadAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    private bool CanSignIn() => !IsBusy;

    private static string GetSignInStatusMessage()
    {
#if MACCATALYST
        return "Opening Microsoft sign-in inside the app. Complete login in that window.";
#else
        return "Starting sign-in...";
#endif
    }

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

    private async Task WarmUpApiAsync()
    {
        try
        {
            await _apiWarmupService.WarmUpAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "API warm-up failed during landing-page initialization.");
        }
    }
}
