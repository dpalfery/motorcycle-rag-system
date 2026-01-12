using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin;

internal partial class App : Application {
    internal App() {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState) {
        // Resolve dependencies from the service provider at window creation time
        var serviceProvider = Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("Service provider not available");

        var authService = serviceProvider.GetRequiredService<IAdminAuthService>();
        var settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        var appShell = serviceProvider.GetRequiredService<AppShell>();

        return new Window(appShell);
    }
}

