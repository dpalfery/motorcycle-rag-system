using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin.Pages;

public partial class ToolsPage : ContentPage {
    public ToolsPage(ToolsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}


