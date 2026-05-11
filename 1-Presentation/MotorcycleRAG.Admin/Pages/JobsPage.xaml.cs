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
    private IDispatcherTimer? _pollTimer;
    private bool _hasInitialized;

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
            else {
                await _viewModel.RefreshAsync();
            }

            _pollTimer.Start();
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
        timer.Interval = TimeSpan.FromSeconds(15);
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
}


