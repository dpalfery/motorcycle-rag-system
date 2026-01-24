using System.Runtime.Versioning;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.ViewModels; // Ensure this using is present

namespace MotorcycleRAG.Admin.Pages;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "S3059:Types should not have members with visibility set higher than the type's visibility",
    Justification = "Internal class has public constructor required by MAUI DI framework")]
internal partial class ToolsPage : ContentPage {
    [SupportedOSPlatform("windows10.0.17763.0")]
    public ToolsPage(MotorcycleRAG.Admin.ViewModels.ToolsViewModel viewModel) {
        InitializeComponent();
#if WINDOWS
        BindingContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
#endif
    }
}


