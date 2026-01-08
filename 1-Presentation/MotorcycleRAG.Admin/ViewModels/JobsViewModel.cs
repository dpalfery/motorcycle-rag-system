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
internal class JobsViewModel : IDisposable {
    private readonly ApiClient _apiClient;
    private readonly IAdminAuthService _authService;
    private readonly ILogger<JobsViewModel> _logger;
    private readonly System.Timers.Timer _pollTimer;
    private bool _isLoading;
    private bool _isPolling;

    public JobsViewModel(ApiClient apiClient, IAdminAuthService authService, ILogger<JobsViewModel> logger) {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
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

    public event PropertyChangedEventHandler? PropertyChanged;

    #region Properties

    public ObservableCollection<JobViewModel> Jobs { get; }

    public bool IsLoading {
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

    public ICommand LoadJobsCommand { get; }
    public ICommand CancelJobCommand { get; }

    #endregion

    #region Lifecycle

    public async Task InitializeAsync() {
        await EnsureAuthorizedAsync();
        await LoadJobsAsync();
        _pollTimer.Start();
    }

    public void StopPolling() {
        _pollTimer.Stop();
    }

    #endregion

    #region Methods

    private async void OnPollTimerElapsed(object? sender, Timers.ElapsedEventArgs e)
    {
        if (_isPolling)
            return;

        _isPolling = true;

        // Poll for updates on running jobs
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await EnsureAuthorizedAsync().ConfigureAwait(false);

                // Filter running jobs using indexer access instead of LINQ
                var runningJobs = new List<JobViewModel>();
                for (int i = 0; i < Jobs.Count; i++)
                {
                    var job = Jobs[i];
                    if (job.IsRunning)
                        runningJobs.Add(job);
                }

                foreach (var job in runningJobs)
                {
                    var statusResponse = await _apiClient.GetPipelineStatusAsync(job.ExecutionId).ConfigureAwait(false);
                    job.Status = statusResponse.Status;

                    // If job completed, reload full job list
                    if (!IsRunningStatus(statusResponse.Status))
                    {
                        await LoadJobsAsync().ConfigureAwait(false);
                        break;
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "User not authorized for polling");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "API not available during polling");
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "Polling operation was cancelled");
            }
            finally
            {
                _isPolling = false;
            }
        }).ConfigureAwait(false);
    }

    private async Task LoadJobsAsync()
    {
        IsLoading = true;
        try
        {
            var executions = await _apiClient.GetPipelineExecutionsAsync().ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Jobs.Clear();
                foreach (var execution in executions.OrderByDescending(e => e.StartTime))
                {
                    Jobs.Add(new JobViewModel
                    {
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
        catch (UnauthorizedAccessException)
        {
            // Don't show error for missing authentication - this is expected in demo mode
            await MainThread.InvokeOnMainThreadAsync(() => Jobs.Clear()).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // API not available - silently fail
            await MainThread.InvokeOnMainThreadAsync(() => Jobs.Clear()).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Operation was cancelled - silently fail
            await MainThread.InvokeOnMainThreadAsync(() => Jobs.Clear()).ConfigureAwait(false);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task CancelJobAsync(string executionId)
    {
        try
        {
            var window = Application.Current?.Windows?.FirstOrDefault();
            if (window?.Page != null)
            {
                var confirm = await window.Page.DisplayAlertAsync(
                    "Cancel Job",
                    $"Are you sure you want to cancel execution {executionId}?",
                    "Yes",
                    "No").ConfigureAwait(false);

                if (confirm)
                {
                    var result = await _apiClient.CancelPipelineAsync(executionId).ConfigureAwait(false);
                    if (result.Cancelled)
                    {
                        await window.Page.DisplayAlertAsync("Success", "Job cancelled successfully", "OK").ConfigureAwait(false);
                        await LoadJobsAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        await window.Page.DisplayAlertAsync("Error", "Failed to cancel job", "OK").ConfigureAwait(false);
                    }
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "User not authorized to cancel job {ExecutionId}", executionId);
        }
        catch (HttpRequestException ex)
        {
            var window = Application.Current?.Windows?.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlertAsync("Error", "Failed to reach the API server", "OK").ConfigureAwait(false);
            }
            _logger.LogWarning(ex, "Failed to reach API server when canceling execution {ExecutionId}", executionId);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Cancel job operation timed out for execution {ExecutionId}", executionId);
        }
    }

    private async Task EnsureAuthorizedAsync()
    {
        if (!_authService.IsSignedIn())
        {
            throw new UnauthorizedAccessException("User is not signed in");
        }

        var roles = await _authService.GetUserRolesAsync().ConfigureAwait(false);
        var isAdmin = AdminRoles.GetValidAdminRoles(roles).Any();
        if (!isAdmin)
        {
            throw new UnauthorizedAccessException("User does not have admin permissions");
        }
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
        if (disposing) {
            // Stop the timer before disposing to prevent race conditions
            if (_pollTimer != null) {
                _pollTimer.Stop();
                _pollTimer.Dispose();
            }
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
internal class JobViewModel : INotifyPropertyChanged
{
    private PipelineStatus _status;

    public string ExecutionId { get; set; } = string.Empty;
    public string PipelineType { get; set; } = string.Empty;

    public PipelineStatus Status {
        get => _status;
        set {
            if (_status != value) {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }
    }

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public ObservableCollection<string> Errors { get; set; } = new();
    public ObservableCollection<string> Warnings { get; set; } = new();
    public bool IsRunning { get; set; }
    public bool HasErrors => Errors.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;

    public event PropertyChangedEventHandler? PropertyChanged;
}


