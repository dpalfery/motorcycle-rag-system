using MotorcycleRAG.Admin.ViewModels;
using MotorcycleRAG.Admin.Services;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Pages;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "S3059:Types should not have members with visibility set higher than the type's visibility",
    Justification = "Internal class has public constructor required by MAUI DI framework")]
internal partial class JobsPage : ContentPage {
    private readonly JobsViewModel _viewModel;
    private readonly ILogger<JobsPage> _logger;

    public JobsPage(JobsViewModel viewModel, ILogger<JobsPage> logger) {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing() {
        base.OnAppearing();
        try {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex) {
                _logger.LogError(ex, "Failed to initialize Jobs page");
                await Utilities.ErrorPresenter.ShowErrorAsync(
                    "Initialization Error",
                    "Failed to load the jobs page. Check Settings for API and local processor configuration."
                );
            }
        }

    protected override void OnDisappearing() {
        base.OnDisappearing();
        _viewModel.StopPolling();
    }
}


