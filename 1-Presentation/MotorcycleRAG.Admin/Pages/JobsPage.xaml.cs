using MotorcycleRAG.Admin.ViewModels;
using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin.Pages;

public partial class JobsPage : ContentPage
{
    private readonly JobsViewModel _viewModel;

    public JobsPage(JobsViewModel viewModel)
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

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopPolling();
    }
}
