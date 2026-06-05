using MotorcycleRAG.Admin.ViewModels;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Pages;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "Internal class has public constructor required by MAUI DI framework")]
internal partial class GraphSeedingPage : ContentPage {
    private static readonly string[] CsvExtensions = [".csv"];
    private readonly GraphSeedingViewModel _viewModel;
    private readonly ILogger<GraphSeedingPage> _logger;
    private bool _hasInitialized;

    public GraphSeedingPage(GraphSeedingViewModel viewModel, ILogger<GraphSeedingPage> logger) {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing() {
        base.OnAppearing();
        try {
            if (!_hasInitialized) {
                await _viewModel.InitializeAsync();
                _hasInitialized = true;
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to initialize Graph Seeding page");
            await Utilities.ErrorPresenter.ShowErrorAsync(
                "Initialization Error",
                "Failed to load the graph seeding page. Check local processor configuration.");
        }
    }

    private async void OnChooseCsvClicked(object? sender, EventArgs e) {
        if (!await _viewModel.BeginFileSelectionAsync()) return;

        try {
            var result = await FilePicker.Default.PickAsync(new PickOptions {
                PickerTitle = "Select a CSV file for graph import",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, CsvExtensions },
                    { DevicePlatform.macOS, CsvExtensions }
                })
            });

            if (result is null) return;

            _viewModel.ApplySelectedFile(result.FullPath);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to select CSV for graph import.");
            await Utilities.ErrorPresenter.ShowErrorAsync(
                "File Selection Error",
                Utilities.ErrorPresenter.SanitizeErrorMessage(ex.Message));
        }
        finally {
            _viewModel.EndFileSelection();
        }
    }
}