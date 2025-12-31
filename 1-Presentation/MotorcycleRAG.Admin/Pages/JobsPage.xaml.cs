using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Domain.DTOs;
using System.Collections.ObjectModel;
using System.Timers;

namespace MotorcycleRAG.Admin.Pages;

public partial class JobsPage : ContentPage
{
    private readonly ApiClient _apiClient;
    private readonly IAdminAuthService _authService;
    private readonly System.Timers.Timer _pollTimer;
    private readonly ObservableCollection<JobViewModel> _jobs;

    public JobsPage(ApiClient apiClient, IAdminAuthService authService)
    {
        InitializeComponent();
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        
        _jobs = new ObservableCollection<JobViewModel>();
        JobsCollectionView.ItemsSource = _jobs;

        // Setup polling timer (every 5 seconds)
        _pollTimer = new System.Timers.Timer(5000);
        _pollTimer.Elapsed += OnPollTimerElapsed;
        _pollTimer.AutoReset = true;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await EnsureAuthorizedAsync();
        await LoadJobsAsync();
        _pollTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _pollTimer.Stop();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await EnsureAuthorizedAsync();
        await LoadJobsAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string executionId)
        {
            var confirm = await DisplayAlertAsync(
                "Cancel Job",
                $"Are you sure you want to cancel execution {executionId}?",
                "Yes",
                "No");

            if (confirm)
            {
                try
                {
                    var result = await _apiClient.CancelPipelineAsync(executionId);
                    if (result.Cancelled)
                    {
                        await DisplayAlertAsync("Success", "Job cancelled successfully", "OK");
                        await LoadJobsAsync();
                    }
                    else
                    {
                        await DisplayAlertAsync("Error", "Failed to cancel job", "OK");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error cancelling job {executionId}: {ex}");
                    await DisplayAlertAsync("Error", $"Error cancelling job: {ex.Message}", "OK");
                }
            }
        }
    }

    private async void OnPollTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        // Poll for updates on running jobs
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await EnsureAuthorizedAsync();
                var runningJobs = _jobs.Where(j => j.IsRunning).ToList();
                foreach (var job in runningJobs)
                {
                    var statusResponse = await _apiClient.GetPipelineStatusAsync(job.ExecutionId);
                    job.Status = statusResponse.Status;

                    // If job completed, reload full job list
                    if (!IsRunningStatus(statusResponse.Status))
                    {
                        await LoadJobsAsync();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error polling job status: {ex}");
                // Silently fail polling - user can manually refresh
            }
        });
    }

    private async Task LoadJobsAsync()
    {
        try
        {
            var executions = await _apiClient.GetPipelineExecutionsAsync();
            
            _jobs.Clear();
            foreach (var execution in executions.OrderByDescending(e => e.StartTime))
            {
                _jobs.Add(new JobViewModel
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
        }
        catch (UnauthorizedAccessException)
        {
            // Don't show error for missing authentication - this is expected in demo mode
            _jobs.Clear();
        }
        catch (HttpRequestException)
        {
            // API not available - silently fail
            _jobs.Clear();
        }
        catch (Exception ex)
        {
            // Only show error for unexpected exceptions
            await DisplayAlertAsync("Error", $"Failed to load jobs: {ex.Message}", "OK");
        }
    }

    private async Task EnsureAuthorizedAsync()
    {
        if (!_authService.IsSignedIn())
        {
            await DisplayAlertAsync("Unauthorized", "Please sign in to view jobs.", "OK");
            throw new UnauthorizedAccessException();
        }

        var roles = await _authService.GetUserRolesAsync();
        var isAdmin = roles.Any(r => r.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                                  || r.Equals("DataAdmin", StringComparison.OrdinalIgnoreCase)
                                  || r.Equals("ContentAdmin", StringComparison.OrdinalIgnoreCase)
                                  || r.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase));
        if (!isAdmin)
        {
            await DisplayAlertAsync("Forbidden", "You do not have permission to view pipeline jobs.", "OK");
            throw new UnauthorizedAccessException();
        }
    }

    private static bool IsRunningStatus(PipelineStatus status)
    {
        return status == PipelineStatus.Processing || 
               status == PipelineStatus.Queued || 
               status == PipelineStatus.Indexing;
    }
}

/// <summary>
/// ViewModel for a pipeline job in the list
/// </summary>
public class JobViewModel
{
    public string ExecutionId { get; set; } = string.Empty;
    public string PipelineType { get; set; } = string.Empty;
    public PipelineStatus Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public ObservableCollection<string> Errors { get; set; } = new();
    public ObservableCollection<string> Warnings { get; set; } = new();
    public bool IsRunning { get; set; }
    public bool HasErrors => Errors.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;
}
