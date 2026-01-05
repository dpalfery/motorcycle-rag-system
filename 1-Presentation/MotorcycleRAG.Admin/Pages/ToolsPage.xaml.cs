using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.ViewModels; // Ensure this using is present

namespace MotorcycleRAG.Admin.Pages;

public partial class ToolsPage : ContentPage
{
    public ToolsPage(MotorcycleRAG.Admin.ViewModels.ToolsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}


