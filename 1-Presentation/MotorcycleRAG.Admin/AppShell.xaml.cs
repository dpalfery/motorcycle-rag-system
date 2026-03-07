using MotorcycleRAG.Admin.Services;

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
    private readonly IAdminAuthService _authService;
    private readonly ISettingsService _settingsService;
    private readonly IConfigurationStateService _configService;

    internal AppShell(IAdminAuthService authService, ISettingsService settingsService, IConfigurationStateService configService, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _ = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        // Register routes for navigation
        RegisterRoutes();

        // Update UI based on auth state
        Loaded += OnShellLoaded;
    }

    private void RegisterRoutes()
    {
        // Register all routes for Shell-based navigation
        Routing.RegisterRoute("dashboard", typeof(Pages.DashboardPage));
        Routing.RegisterRoute("dashboardpage", typeof(Pages.DashboardPage));
        Routing.RegisterRoute("uploadpage", typeof(Pages.UploadPage));
        Routing.RegisterRoute("jobspage", typeof(Pages.JobsPage));
        Routing.RegisterRoute("websourcespage", typeof(Pages.WebSourcesPage));
        Routing.RegisterRoute("toolspage", typeof(Pages.ToolsPage));
        Routing.RegisterRoute("settingspage", typeof(Pages.SettingsPage));
    }

    private async void OnShellLoaded(object? sender, EventArgs e)
    {
        // Load configuration on startup
        try
        {
            await _configService.LoadConfigurationAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading configuration: {ex.Message}");
        }

        await UpdateUIAsync();
    }

    private async void OnAuthButtonClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_authService.IsSignedIn())
            {
                // Sign out from auth service
                await _authService.SignOutAsync();

                // Clear any cached tokens
                await _settingsService.RemoveSecureAsync("auth_token");
                await _settingsService.RemoveSecureAsync("auth_refresh_token");

                // Navigate back to main page or splash
                await Shell.Current.GoToAsync("//");
            }
            else
            {
                // Sign in
                await _authService.SignInAsync();
            }
            
            await UpdateUIAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Auth error: {ex.Message}");
            await DisplayAlertAsync("Authentication Error", $"Action failed: {ex.Message}", "OK");
        }
    }

    private async Task UpdateUIAsync()
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            try
            {
                var isSignedIn = _authService.IsSignedIn();

                // Update user display name in TitleView
                if (isSignedIn)
                {
                    var displayName = _authService.UserDisplayName;
                    UserDisplayName.Text = displayName ?? "User";
                    AuthButton.Text = "Sign Out";
                }
                else
                {
                    UserDisplayName.Text = "Not signed in";
                    AuthButton.Text = "Sign In";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating UI: {ex.Message}");
                UserDisplayName.Text = "User";
            }
        });
    }
}


