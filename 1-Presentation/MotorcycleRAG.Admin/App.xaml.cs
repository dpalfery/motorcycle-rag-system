using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin;

public partial class App : Application
{
	private readonly IAdminAuthService _authService;

	public App()
	{
		InitializeComponent();
		
		// TODO: Replace with actual configuration from appsettings.json or environment
		_authService = new AdminAuthService(
			clientId: Environment.GetEnvironmentVariable("ENTRA_CLIENT_ID") ?? "YOUR_CLIENT_ID",
			authority: Environment.GetEnvironmentVariable("ENTRA_AUTHORITY") ?? "https://login.microsoftonline.com/YOUR_TENANT_ID",
			scopes: new[] { Environment.GetEnvironmentVariable("API_SCOPE") ?? "api://YOUR_API_ID/.default" }
		);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell(_authService));
	}
}
