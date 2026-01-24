using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.ViewModels;

namespace MotorcycleRAG.Admin.Pages;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "S3059:Types should not have members with visibility set higher than the type's visibility",
    Justification = "Internal class has public constructor required by MAUI DI framework")]
internal partial class WebSourcesPage : ContentPage {
    private readonly WebSourcesViewModel _viewModel;

    public WebSourcesPage(WebSourcesViewModel viewModel) {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing() {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }
}


