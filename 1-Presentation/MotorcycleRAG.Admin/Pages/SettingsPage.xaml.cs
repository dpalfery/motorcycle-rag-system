using MotorcycleRAG.Admin.ViewModels;

namespace MotorcycleRAG.Admin.Pages;

/// <summary>
/// Settings page for configuring API, authentication, and local processing options.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by MAUI framework via DI")]
internal partial class SettingsPage : ContentPage {
    public SettingsPage(SettingsViewModel viewModel) {
        InitializeComponent();
        BindingContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
