using MotorcycleRAG.Admin.ViewModels;

namespace MotorcycleRAG.Admin.Pages;

internal partial class LandingPage : ContentPage
{
    private readonly LandingViewModel _viewModel;

    public LandingPage(LandingViewModel viewModel)
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
