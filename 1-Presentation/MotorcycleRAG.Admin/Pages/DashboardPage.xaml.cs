using MotorcycleRAG.Admin.ViewModels;

namespace MotorcycleRAG.Admin.Pages;

internal partial class DashboardPage : ContentPage {
    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}


