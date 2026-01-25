using System.Runtime.Versioning;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.ViewModels; // Ensure this using is present
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Pages;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "S3059:Types should not have members with visibility set higher than the type's visibility",
    Justification = "Internal class has public constructor required by MAUI DI framework")]
internal partial class ToolsPage : ContentPage {
    private readonly MotorcycleRAG.Admin.ViewModels.ToolsViewModel? _viewModel;
    private readonly ILogger<ToolsPage>? _logger;

    [SupportedOSPlatform("windows10.0.17763.0")]
    public ToolsPage(MotorcycleRAG.Admin.ViewModels.ToolsViewModel viewModel, ILogger<ToolsPage> logger) {
        InitializeComponent();
#if WINDOWS
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        BindingContext = _viewModel;
#endif
    }

    protected override async void OnAppearing() {
        base.OnAppearing();
        
#if WINDOWS
        if (_viewModel != null) {
            try {
                await _viewModel.InitializeAsync();
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Failed to initialize Tools page");
                await Utilities.ErrorPresenter.ShowErrorAsync(
                    "Initialization Error",
                    "Failed to load MCP tools. Please check Settings to ensure API and authentication are configured."
                );
            }
        }
#endif
    }
}


