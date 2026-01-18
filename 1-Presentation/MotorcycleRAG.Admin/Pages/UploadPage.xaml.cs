using MotorcycleRAG.Admin.ViewModels;

namespace MotorcycleRAG.Admin.Pages;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI framework")]
internal partial class UploadPage : ContentPage {
    public UploadPage(IngestionViewModel viewModel) {
        InitializeComponent();
        BindingContext = viewModel;
    }

    public UploadPage() {
        InitializeComponent();
    }
}


