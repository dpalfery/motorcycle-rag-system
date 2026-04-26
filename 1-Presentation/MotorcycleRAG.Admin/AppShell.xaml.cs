using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Utilities;

namespace MotorcycleRAG.Admin;

/// <summary>
/// Application shell for MAUI admin application.
///
/// SECURITY NOTE: UI hiding based on roles is a convenience feature only and provides
/// NO security guarantees. Authorization validation MUST be enforced at the API layer
/// for all protected operations. The API endpoints must independently validate that the
/// authenticated user has the required permissions before processing requests.
///
/// Do not rely on UI visibility for security. Always validate on the server side.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI framework")]
internal partial class AppShell : Shell {
    private const double HeaderMinimumWidth = 320d;
    private readonly IAdminAuthService _authService;
    private readonly ISettingsService _settingsService;
    private readonly IConfigurationStateService _configService;
    private readonly IAppFlowCoordinator _appFlowCoordinator;

    internal AppShell(IAdminAuthService authService, ISettingsService settingsService, IConfigurationStateService configService, IServiceProvider serviceProvider) {
        InitializeComponent();
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _appFlowCoordinator = serviceProvider?.GetRequiredService<IAppFlowCoordinator>()
            ?? throw new ArgumentNullException(nameof(serviceProvider));

        // Assign page instances directly from DI.
        // ContentTemplate="{DataTemplate T}" calls Activator.CreateInstance(T) which bypasses DI
        // and requires a parameterless constructor — these pages use constructor injection.
        // Setting ShellContent.Content directly after InitializeComponent is the correct pattern.
        DashboardContent.Content = serviceProvider.GetRequiredService<Pages.DashboardPage>();
        UploadContent.Content = serviceProvider.GetRequiredService<Pages.UploadPage>();
        JobsContent.Content = serviceProvider.GetRequiredService<Pages.JobsPage>();
        UserManagementContent.Content = serviceProvider.GetRequiredService<Pages.UserManagementPage>();
        WebSourcesContent.Content = serviceProvider.GetRequiredService<Pages.WebSourcesPage>();
        ToolsContent.Content = serviceProvider.GetRequiredService<Pages.ToolsPage>();
        SettingsContent.Content = serviceProvider.GetRequiredService<Pages.SettingsPage>();

        // Update UI based on auth state
        Loaded += OnShellLoaded;
        SizeChanged += OnShellSizeChanged;
    }

    private async void OnShellLoaded(object? sender, EventArgs e) {
#if WINDOWS
        ConfigureWindowsShellChrome();
#endif

        // Load configuration on startup
        try {
            await MauiThreading.RunOffMainThreadAsync(() => _configService.LoadConfigurationAsync()).ConfigureAwait(false);
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Error loading configuration: {ex.Message}");
        }

        await UpdateUIAsync().ConfigureAwait(false);
    }

    private void OnShellSizeChanged(object? sender, EventArgs e) {
        UpdateHeaderWidth();

#if WINDOWS
        ConfigureWindowsShellChrome();
#endif
    }

    protected override void OnHandlerChanged() {
        base.OnHandlerChanged();

#if WINDOWS
        ConfigureWindowsShellChrome();
#endif
    }

    private async void OnAuthButtonClicked(object? sender, EventArgs e) {
        try {
            if (_authService.IsSignedIn()) {
                await MauiThreading.RunOffMainThreadAsync(async () => {
                    await _authService.SignOutAsync().ConfigureAwait(false);
                    await _settingsService.RemoveSecureAsync("auth_token").ConfigureAwait(false);
                    await _settingsService.RemoveSecureAsync("auth_refresh_token").ConfigureAwait(false);
                }).ConfigureAwait(false);

                _appFlowCoordinator.ShowLandingPage();
            }
            else {
                // Sign in
                var signedIn = await MauiThreading.RunOffMainThreadAsync(() => _authService.SignInAsync()).ConfigureAwait(false);
                if (!signedIn) {
                    return;
                }
            }

            await UpdateUIAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Auth error: {ex.Message}");
            await ErrorPresenter.ShowErrorAsync("Authentication Error", $"Action failed: {ex.Message}").ConfigureAwait(false);
        }
    }

    private async Task UpdateUIAsync() {
        await MainThread.InvokeOnMainThreadAsync(() => {
            try {
                var isSignedIn = _authService.IsSignedIn();

                // Update user display name in TitleView
                if (isSignedIn) {
                    var displayName = _authService.UserDisplayName;
                    UserDisplayName.Text = displayName ?? "User";
                    AuthButton.Text = "Sign Out";
                }
                else {
                    UserDisplayName.Text = "Not signed in";
                    AuthButton.Text = "Sign In";
                }
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Error updating UI: {ex.Message}");
                UserDisplayName.Text = "User";
            }
        });
    }

    internal Task RefreshAuthStateAsync() => UpdateUIAsync();

    private void UpdateHeaderWidth() {
        if (Width <= 0) {
            return;
        }

        HeaderGrid.WidthRequest = Math.Max(HeaderMinimumWidth, Width);
    }

#if WINDOWS
    private void ConfigureWindowsShellChrome() {
        MainThread.BeginInvokeOnMainThread(async () => {
            UpdateHeaderWidth();
            StretchCommandBarAndHideOverflow(Handler?.PlatformView as Microsoft.UI.Xaml.DependencyObject, Width);

            await Task.Delay(100);
            StretchCommandBarAndHideOverflow(Handler?.PlatformView as Microsoft.UI.Xaml.DependencyObject, Width);
        });
    }

    private static void StretchCommandBarAndHideOverflow(Microsoft.UI.Xaml.DependencyObject? root, double shellWidth) {
        if (root == null) {
            return;
        }

        if (root is Microsoft.UI.Xaml.Controls.CommandBar commandBar) {
            commandBar.IsDynamicOverflowEnabled = false;
            commandBar.OverflowButtonVisibility = Microsoft.UI.Xaml.Controls.CommandBarOverflowButtonVisibility.Collapsed;
            commandBar.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;

            if (shellWidth > 0) {
                commandBar.MinWidth = shellWidth;
            }

            if (commandBar.Content is Microsoft.UI.Xaml.FrameworkElement content) {
                content.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
                if (shellWidth > 0) {
                    content.Width = shellWidth;
                }
            }
        }
        else if (root is Microsoft.UI.Xaml.FrameworkElement element
            && (element.Name.Equals("MoreButton", StringComparison.OrdinalIgnoreCase)
                || element.Name.Contains("Overflow", StringComparison.OrdinalIgnoreCase))) {
            element.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++) {
            StretchCommandBarAndHideOverflow(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index), shellWidth);
        }
    }
#endif
}
