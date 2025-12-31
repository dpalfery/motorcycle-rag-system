using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin;

public partial class App : Application
{
	private readonly IAdminAuthService _authService;
	private readonly IServiceProvider _serviceProvider;

	public App(IAdminAuthService authService, IServiceProvider serviceProvider)
	{
		InitializeComponent();
		_authService = authService;
		_serviceProvider = serviceProvider;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell(_authService, _serviceProvider));
	}
}
