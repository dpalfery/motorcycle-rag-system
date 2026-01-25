using System.Collections.ObjectModel;
using System.Windows.Input;
using System.ComponentModel;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Utilities;
using MotorcycleRAG.Admin.Constants;
using MotorcycleRAG.Contracts.Models.DTOs;
using Microsoft.Extensions.Logging;
using Timers = System.Timers;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for pipeline jobs management.
/// Handles loading, polling, and canceling pipeline executions.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "Internal patterns")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S103:LineLength", Justification = "Due to suppressions")]
internal class JobsViewModel : IDisposable, INotifyPropertyChanged {
    private readonly ApiClient _apiClient;
    private readonly IAdminAuthService _authService;
    private readonly IConfigurationStateService _configService;
    private readonly ILogger<JobsViewModel> _logger;
    private readonly System.Timers.Timer _pollTimer;
    private bool _isLoading;
    private bool _isPolling;

    public JobsViewModel(ApiClient apiClient, IAdminAuthService authService, IConfigurationStateService configService, ILogger<JobsViewModel> logger) {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Jobs = new ObservableCollection<JobViewModel>();

        // Setup polling timer (every 5 seconds)
        _pollTimer = new System.Timers.Timer(5000);
        _pollTimer.Elapsed += OnPollTimerElapsed;
        _pollTimer.AutoReset = true;

        // Commands
        LoadJobsCommand = new Command(async () => await LoadJobsAsync());
        CancelJobCommand = new Command<string>(async (executionId) => await CancelJobAsync(executionId));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public event PropertyChangedEventHandler? PropertyChanged;

    #region Properties

    internal ObservableCollection<JobViewModel> Jobs { get; }

    internal bool IsLoading {
        get => _isLoading;
        set {
            if (_isLoading != value) {
                _isLoading = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
            }
        }
    }

    #endregion

    #region Commands

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    internal ICommand LoadJobsCommand { get; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    internal ICommand CancelJobCommand { get; }

    #endregion

    #region Lifecycle

    public async Task InitializeAsync() {
        var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
        if (!authorized) {
            return;
        }
        await LoadJobsAsync().ConfigureAwait(false);
        _pollTimer.Start();
    }

    public void StopPolling() {
        _pollTimer.Stop();
    }

    #endregion

    #region Methods

    private async void OnPollTimerElapsed(object? sender, Timers.ElapsedEventArgs e) {
        if (_isPolling)
            return;

        _isPolling = true;

        // Poll for updates on running jobs
        await MainThread.InvokeOnMainThreadAsync(async () => {
            try {
                var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
                if (!authorized) {
                    return;
                }

                // Filter running jobs using indexer access instead of LINQ
                var runningJobs = new List<JobViewModel>();
                for (int i = 0; i < Jobs.Count; i++) {
                    var job = Jobs[i];
                    if (job.IsRunning)
                        runningJobs.Add(job);
                }

                foreach (var job in runningJobs) {
                    var statusResponse = await _apiClient.GetPipelineStatusAsync(job.ExecutionId).ConfigureAwait(false);
                    job.Status = statusResponse.Status;

                    // If job completed, reload full job list and stop polling other jobs
                    if (!IsRunningStatus(statusResponse.Status)) {
                        await LoadJobsAsync().ConfigureAwait(false);
                        return; // Exit early - LoadJobsAsync will refresh all jobs
                    }
                }
            }
            catch (HttpRequestException ex) {
                _logger.LogWarning(ex, "API not available during polling");
            }
            catch (OperationCanceledException ex) {
                _logger.LogWarning(ex, "Polling operation was cancelled");
            }
            finally {
                _isPolling = false;
            }
        }).ConfigureAwait(false);
    }

    private async Task LoadJobsAsync() {
        IsLoading = true;
        try {
            var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
            if (!authorized) {
                return;
            }
            var executions = await _apiClient.GetPipelineExecutionsAsync(default).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() => {
                Jobs.Clear();
                foreach (var execution in executions.OrderByDescending(e => e.StartTime)) {
                    Jobs.Add(new JobViewModel {
                        ExecutionId = execution.ExecutionId,
                        PipelineType = execution.PipelineType,
                        Status = execution.Status,
                        StartTime = execution.StartTime,
                        EndTime = execution.EndTime,
                        CreatedBy = execution.CreatedBy,
                        Errors = new ObservableCollection<string>(execution.Errors),
                        Warnings = new ObservableCollection<string>(execution.Warnings),
                        IsRunning = IsRunningStatus(execution.Status)
                    });
                }
            }).ConfigureAwait(false);
        }
        catch (HttpRequestException) {
            // API not available - silently fail
            await MainThread.InvokeOnMainThreadAsync(() => Jobs.Clear()).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            // Operation was cancelled - silently fail
            await MainThread.InvokeOnMainThreadAsync(() => Jobs.Clear()).ConfigureAwait(false);
        }
        finally {
            IsLoading = false;
        }
    }

    private async Task CancelJobAsync(string executionId) {
        try {
            var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
            if (window?.Page != null) {
                var confirm = await window.Page.DisplayAlertAsync(
                    "Cancel Job",
                    $"Are you sure you want to cancel execution {executionId}?",
                    "Yes",
                    "No").ConfigureAwait(false);

                if (confirm) {
                    var result = await _apiClient.CancelPipelineAsync(executionId).ConfigureAwait(false);
                    if (result.Cancelled) {
                        await window.Page.DisplayAlertAsync("Success", "Job cancelled successfully", "OK").ConfigureAwait(false);
                        await LoadJobsAsync().ConfigureAwait(false);
                    }
                    else {
                        await window.Page.DisplayAlertAsync("Error", "Failed to cancel job", "OK").ConfigureAwait(false);
                    }
                }
            }
        }
        catch (UnauthorizedAccessException ex) {
            _logger.LogWarning(ex, "User not authorized to cancel job {ExecutionId}", executionId);
        }
        catch (HttpRequestException ex) {
            var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
            if (window?.Page != null) {
                await window.Page.DisplayAlertAsync("Error", "Failed to reach the API server", "OK").ConfigureAwait(false);
            }
            _logger.LogWarning(ex, "Failed to reach API server when canceling execution {ExecutionId}", executionId);
        }
        catch (OperationCanceledException ex) {
            _logger.LogWarning(ex, "Cancel job operation timed out for execution {ExecutionId}", executionId);
        }
    }

    private async Task<bool> EnsureAuthorizedAsync() {
        if (!_configService.IsApiConfigured) {
            _logger.LogWarning("Jobs page blocked: API not configured");
            await ErrorPresenter.ShowWarningAsync(
                "Configuration Required",
                "API is not configured. Go to Settings to configure the API base URL."
            ).ConfigureAwait(false);
            return false;
        }

        if (!_authService.IsSignedIn()) {
            _logger.LogWarning("Jobs page blocked: user not signed in");
            await ErrorPresenter.ShowWarningAsync(
                "Sign In Required",
                "Please sign in to view pipeline jobs."
            ).ConfigureAwait(false);
            return false;
        }

        // Use IsAuthorizedAdminAsync to support Debug mode bypass
        var isAuthorized = await _authService.IsAuthorizedAdminAsync().ConfigureAwait(false);
        if (!isAuthorized) {
            _logger.LogWarning("Jobs page blocked: user lacks admin permissions");
            await ErrorPresenter.ShowWarningAsync(
                "Access Denied",
                "You do not have permission to view pipeline jobs."
            ).ConfigureAwait(false);
            return false;
        }

        return true;
    }

    private static bool IsRunningStatus(PipelineStatus status) {
        return status == PipelineStatus.Processing ||
               status == PipelineStatus.Queued ||
               status == PipelineStatus.Indexing;
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (disposing && _pollTimer != null) {
            // Stop the timer before disposing to prevent race conditions
            _pollTimer.Stop();
            _pollTimer.Dispose();
        }
    }

    ~JobsViewModel() {
        Dispose(false);
    }

    #endregion
}

/// <summary>
/// ViewModel for a pipeline job in the list
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "Internal patterns")]
internal class JobViewModel : INotifyPropertyChanged {
    private PipelineStatus _status;

    internal string ExecutionId { get; set; } = string.Empty;
    internal string PipelineType { get; set; } = string.Empty;

    internal PipelineStatus Status {
        get => _status;
        set {
            if (_status != value) {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }
    }

    internal DateTime StartTime { get; set; }
    internal DateTime? EndTime { get; set; }
    internal string CreatedBy { get; set; } = string.Empty;
    internal ObservableCollection<string> Errors { get; init; } = new();
    internal ObservableCollection<string> Warnings { get; init; } = new();
    internal bool IsRunning { get; set; }
    internal bool HasErrors => Errors.Count > 0;
    internal bool HasWarnings => Warnings.Count > 0;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public event PropertyChangedEventHandler? PropertyChanged;
}


