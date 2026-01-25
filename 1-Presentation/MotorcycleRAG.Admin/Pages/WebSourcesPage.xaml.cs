using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.ViewModels;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Pages;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "S3059:Types should not have members with visibility set higher than the type's visibility",
    Justification = "Internal class has public constructor required by MAUI DI framework")]
internal partial class WebSourcesPage : ContentPage {
    private readonly WebSourcesViewModel _viewModel;
    private readonly ILogger<WebSourcesPage> _logger;

    public WebSourcesPage(WebSourcesViewModel viewModel, ILogger<WebSourcesPage> logger) {
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
            _logger.LogError(ex, "Failed to initialize Web Sources page");
            await Utilities.ErrorPresenter.ShowErrorAsync(
                "Initialization Error",
                "Failed to load web sources. Please check Settings to ensure API and authentication are configured."
            );
        }
    }
}


