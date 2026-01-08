using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.ViewModels;

namespace MotorcycleRAG.Admin.Pages;

internal partial class WebSourcesPage : ContentPage {
    private readonly WebSourcesViewModel _viewModel;

    public WebSourcesPage(WebSourcesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }
}


