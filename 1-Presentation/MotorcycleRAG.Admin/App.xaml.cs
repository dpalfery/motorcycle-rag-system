using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin;

public partial class App : Application {
    private readonly IAdminAuthService _authService;
    private readonly ISettingsService _settingsService;
    private readonly IServiceProvider _serviceProvider;

    public App(IAdminAuthService authService, ISettingsService settingsService, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell(_authService, _settingsService, _serviceProvider));
    }
}

