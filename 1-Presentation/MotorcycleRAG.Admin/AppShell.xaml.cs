using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Pages;

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
public partial class AppShell : Shell
{
	private readonly IAdminAuthService _authService;
	private readonly IServiceProvider _serviceProvider;
	private ShellContent? _uploadTab;
	private ShellContent? _jobsTab;

	public AppShell(IAdminAuthService authService, IServiceProvider serviceProvider)
	{
		InitializeComponent();
		_authService = authService;
		_serviceProvider = serviceProvider;

		// Build tabs programmatically using DI
		BuildTabs();

		// Apply role-based visibility
		Loaded += OnShellLoaded;
	}
	
	private void BuildTabs()
	{
		// Create Upload tab
		_uploadTab = new ShellContent
		{
			Title = "Upload",
			Route = "UploadPage",
			ContentTemplate = new DataTemplate(() => _serviceProvider.GetRequiredService<UploadPage>())
		};
		MainTabBar.Items.Add(_uploadTab);
		
		// Create Jobs tab
		_jobsTab = new ShellContent
		{
			Title = "Jobs",
			Route = "JobsPage",
			ContentTemplate = new DataTemplate(() => _serviceProvider.GetRequiredService<JobsPage>())
		};
		MainTabBar.Items.Add(_jobsTab);
	}
	
	private async void OnShellLoaded(object? sender, EventArgs e)
	{
		await ApplyRoleBasedVisibilityAsync();
	}
	
	private async Task ApplyRoleBasedVisibilityAsync()
	{
		if (!_authService.IsSignedIn())
		{
			// Hide all admin tabs if not signed in
			if (_uploadTab != null) _uploadTab.IsVisible = false;
			if (_jobsTab != null) _jobsTab.IsVisible = false;
			return;
		}

		try
		{
			var roles = await _authService.GetUserRolesAsync();
			var roleList = roles.ToList();

			// Check for admin roles (Admin, DataAdmin, ContentAdmin, SuperAdmin)
			// NOTE: This is UI-only visibility. All protected endpoints must validate
			// authorization independently on the server side. Do not depend on this
			// client-side check for security.
			bool isAdmin = roleList.Any(r =>
				r.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
				r.Equals("DataAdmin", StringComparison.OrdinalIgnoreCase) ||
				r.Equals("ContentAdmin", StringComparison.OrdinalIgnoreCase) ||
				r.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase));

			// Show/hide tabs based on roles (UI convenience only, not security)
			if (_uploadTab != null) _uploadTab.IsVisible = isAdmin;
			if (_jobsTab != null) _jobsTab.IsVisible = isAdmin;
		}
		catch
		{
			// On error, hide admin tabs for safety
			if (_uploadTab != null) _uploadTab.IsVisible = false;
			if (_jobsTab != null) _jobsTab.IsVisible = false;
		}
	}
}
