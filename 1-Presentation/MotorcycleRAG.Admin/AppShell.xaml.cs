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
internal partial class AppShell : Shell {
    private readonly IAdminAuthService _authService;
    private readonly ISettingsService _settingsService;

    public AppShell(IAdminAuthService authService, ISettingsService settingsService, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
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
    }

    private async void OnShellLoaded(object? sender, EventArgs e)
    {
        await UpdateUIAsync();
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        try
        {
            // Sign out from auth service
            await _authService.SignOutAsync();

            // Clear any cached tokens
            await _settingsService.RemoveSecureAsync("auth_token");
            await _settingsService.RemoveSecureAsync("auth_refresh_token");

            // Navigate back to main page or splash
            await Shell.Current.GoToAsync("//");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sign out error: {ex.Message}");
            await DisplayAlertAsync("Sign Out Error", "Failed to sign out. Please try again.", "OK");
        }
    }

    private async Task UpdateUIAsync()
    {
        try
        {
            // Update user display name in TitleView
            if (_authService.IsSignedIn())
            {
                var displayName = _authService.GetUserDisplayName();
                UserDisplayName.Text = displayName ?? "User";
            }
            else
            {
                UserDisplayName.Text = "Not signed in";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating UI: {ex.Message}");
            UserDisplayName.Text = "User";
        }
        await Task.CompletedTask;
    }
}


