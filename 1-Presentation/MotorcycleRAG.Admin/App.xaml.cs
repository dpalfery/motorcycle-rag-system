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
    private readonly IServiceProvider _serviceProvider;

    public App(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState) {
        // Resolve AppShell with all its dependencies
        var appShell = _serviceProvider.GetRequiredService<AppShell>();
        return new Window(appShell);
    }
}

