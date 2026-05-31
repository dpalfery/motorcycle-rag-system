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
    private bool _isPageVisible;
    private bool _pollRefreshInProgress;

    public JobsPage(JobsViewModel viewModel, ILogger<JobsPage> logger) {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing() {
        base.OnAppearing();
        _isPageVisible = true;
        try {
            _pollTimer ??= CreatePollTimer();

            if (!_hasInitialized) {
                await Task.Delay(100);
                if (!_isPageVisible) {
                    return;
                }

                await _viewModel.InitializeAsync();
                _hasInitialized = true;
            }
            else {
                await _viewModel.RefreshAsync();
            }

            if (_isPageVisible) {
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

    protected override async void OnDisappearing() {
        base.OnDisappearing();
        _isPageVisible = false;
        _pollTimer?.Stop();
        try {
            await _viewModel.StopRefreshAsync();
        }
        catch (ObjectDisposedException ex) {
            _logger.LogDebug(ex, "Jobs page refresh state was already disposed.");
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Jobs page refresh did not stop cleanly.");
        }
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
        if (!_isPageVisible || _pollRefreshInProgress) {
            return;
        }

        _pollRefreshInProgress = true;
        try {
            await _viewModel.RefreshAsync();
        }
        catch (OperationCanceledException ex) {
            _logger.LogDebug(ex, "Jobs page polling was cancelled.");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Jobs page polling failed.");
        }
        finally {
            _pollRefreshInProgress = false;
        }
    }
}


