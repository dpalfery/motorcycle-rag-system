using MotorcycleRAG.Admin.ViewModels;

namespace MotorcycleRAG.Admin.Pages;

internal partial class UploadPage : ContentPage {
    public UploadPage(IngestionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    public UploadPage()
    {
        InitializeComponent();
    }
}


