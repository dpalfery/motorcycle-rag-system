using System.Runtime.Versioning;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.ViewModels; // Ensure this using is present

namespace MotorcycleRAG.Admin.Pages;

internal partial class ToolsPage : ContentPage
{
    [SupportedOSPlatform("windows10.0.17763.0")]
    public ToolsPage(MotorcycleRAG.Admin.ViewModels.ToolsViewModel viewModel)
    {
        InitializeComponent();
#if WINDOWS
        BindingContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
#endif
    }
}


