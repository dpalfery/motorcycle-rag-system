using MotorcycleRAG.Admin.ViewModels;

namespace MotorcycleRAG.Admin.Pages;

internal partial class UploadPage : ContentPage {
    internal UploadPage(IngestionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    internal UploadPage()
    {
        InitializeComponent();
    }
}


