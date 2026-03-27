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
    private static readonly string[] CsvExtensions = [".csv"];
    private readonly JobsViewModel _viewModel;
    private readonly ILogger<JobsPage> _logger;
    private IDispatcherTimer? _pollTimer;
    private bool _hasInitialized;
    private bool _isPickerOpen;

    public JobsPage(JobsViewModel viewModel, ILogger<JobsPage> logger) {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing() {
        base.OnAppearing();
        try {
            _pollTimer ??= CreatePollTimer();

            if (!_hasInitialized) {
                await _viewModel.InitializeAsync();
                _hasInitialized = true;
            }
            else if (!_isPickerOpen) {
                await _viewModel.RefreshAsync();
            }

            if (!_isPickerOpen) {
                _pollTimer.Start();
            }
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
        _pollTimer?.Stop();
    }

    private IDispatcherTimer CreatePollTimer() {
        var dispatcher = Dispatcher ?? throw new InvalidOperationException("Dispatcher is not available for JobsPage polling.");
        var timer = dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(5);
        timer.IsRepeating = true;
        timer.Tick += OnPollTimerTick;
        return timer;
    }

    private async void OnPollTimerTick(object? sender, EventArgs e) {
        try {
            await _viewModel.RefreshAsync();
        }
        catch (OperationCanceledException ex) {
            _logger.LogDebug(ex, "Jobs page polling was cancelled.");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Jobs page polling failed.");
        }
    }

    private async void OnChooseCsvClicked(object? sender, EventArgs e) {
        _isPickerOpen = true;
        _pollTimer?.Stop();

        if (!await _viewModel.BeginLocalGraphFileSelectionAsync()) {
            _isPickerOpen = false;
            _pollTimer?.Start();
            return;
        }

        try {
            var result = await FilePicker.Default.PickAsync(new PickOptions {
                PickerTitle = "Select a CSV file for graph import",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, CsvExtensions }
                })
            });

            if (result is null) {
                return;
            }

            _viewModel.ApplySelectedLocalGraphFile(result.FullPath);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to select a local CSV for graph import.");
            await Utilities.ErrorPresenter.ShowErrorAsync(
                "File Selection Error",
                Utilities.ErrorPresenter.SanitizeErrorMessage(ex.Message));
        }
        finally {
            _viewModel.EndLocalGraphFileSelection();
            _isPickerOpen = false;
            _pollTimer?.Start();
        }
    }
}


