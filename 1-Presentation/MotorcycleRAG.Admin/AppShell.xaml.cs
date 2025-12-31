using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin;

public partial class AppShell : Shell
{
	private readonly IAdminAuthService _authService;

	public AppShell(IAdminAuthService authService)
	{
		InitializeComponent();
		_authService = authService;
		
		// Apply role-based visibility
		Loaded += OnShellLoaded;
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
			UploadTab.IsVisible = false;
			JobsTab.IsVisible = false;
			return;
		}
		
		try
		{
			var roles = await _authService.GetUserRolesAsync();
			var roleList = roles.ToList();
			
			// Check for admin roles (Admin, DataAdmin, ContentAdmin, SuperAdmin)
			bool isAdmin = roleList.Any(r => 
				r.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
				r.Equals("DataAdmin", StringComparison.OrdinalIgnoreCase) ||
				r.Equals("ContentAdmin", StringComparison.OrdinalIgnoreCase) ||
				r.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase));
			
			// Show/hide tabs based on roles
			UploadTab.IsVisible = isAdmin;
			JobsTab.IsVisible = isAdmin;
		}
		catch
		{
			// On error, hide admin tabs for safety
			UploadTab.IsVisible = false;
			JobsTab.IsVisible = false;
		}
	}
}
