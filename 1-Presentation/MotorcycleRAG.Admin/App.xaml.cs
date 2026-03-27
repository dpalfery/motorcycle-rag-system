using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "S3059:Types should not have members with visibility set higher than the type's visibility",
    Justification = "Internal class has public constructor required by MAUI DI framework")]
internal partial class App : Application {
    private readonly IAppFlowCoordinator _appFlowCoordinator;

    public App() {
        InitializeComponent();

        // Get required services from DI container
        // MauiProgram.cs has already configured all dependencies including
        // IAdminAuthService, ISettingsService, IConfigurationStateService, and logging
        var serviceProvider = IPlatformApplication.Current?.Services
            ?? throw new InvalidOperationException("ServiceProvider not available during App construction");

        var authService = serviceProvider.GetRequiredService<IAdminAuthService>();
        var settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        var configService = serviceProvider.GetRequiredService<IConfigurationStateService>();
        _ = authService;
        _ = settingsService;
        _ = configService;

        _appFlowCoordinator = serviceProvider.GetRequiredService<IAppFlowCoordinator>();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_appFlowCoordinator.CreateLandingRootPage());
    }
}
