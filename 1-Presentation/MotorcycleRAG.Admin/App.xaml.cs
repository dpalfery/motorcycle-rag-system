using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI framework")]
internal partial class App : Application {
    internal App() {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState) {
        // Resolve dependencies from the service provider at window creation time
        var serviceProvider = Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("Service provider not available");

        var appShell = serviceProvider.GetRequiredService<AppShell>();

        return new Window(appShell);
    }
}

