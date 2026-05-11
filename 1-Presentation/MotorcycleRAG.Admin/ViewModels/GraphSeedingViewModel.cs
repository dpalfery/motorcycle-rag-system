using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Utilities;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for the dedicated Graph Seeding page.
/// Handles CSV file selection, validation, launching local graph seeding jobs,
/// and monitoring graph seeding job history.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "For data binding")]
internal class GraphSeedingViewModel : IDisposable, INotifyPropertyChanged {
    private const long MaxFileSizeBytes = 2L * 1024 * 1024 * 1024;

    private readonly ApiClient _apiClient;
    private readonly IAdminAuthService _authService;
    private readonly IConfigurationStateService _configService;
    private readonly ILocalProcessorService _localProcessorService;
    private readonly ILogger<GraphSeedingViewModel> _logger;

    private readonly SemaphoreSlim _loadJobsSemaphore = new(1, 1);
    private bool _isStartingGraphJob;
    private bool _isLoadingJobs;
    private string _selectedFilePath = string.Empty;
    private string _statusMessage = string.Empty;

    public GraphSeedingViewModel(
        ApiClient apiClient,
        IAdminAuthService authService,
        IConfigurationStateService configService,
        ILocalProcessorService localProcessorService,
        ILogger<GraphSeedingViewModel> logger) {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _localProcessorService = localProcessorService ?? throw new ArgumentNullException(nameof(localProcessorService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        LocalProcessorJobs = new ObservableCollection<LocalProcessorJobViewModel>();
        StartGraphJobCommand = new AsyncRelayCommand(StartGraphJobAsync);
        RefreshJobsCommand = new AsyncRelayCommand(() => LoadJobsAsync(forceRefresh: true));
        ImportGraphJobCommand = new AsyncRelayCommand<LocalProcessorJobViewModel>(ImportGraphJobAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LocalProcessorJobViewModel> LocalProcessorJobs { get; }

    public bool IsStartingGraphJob {
        get => _isStartingGraphJob;
        private set {
            if (_isStartingGraphJob == value) return;
            _isStartingGraphJob = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStartingGraphJob)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelectFile)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartGraphJob)));
        }
    }

    public bool IsLoadingJobs {
        get => _isLoadingJobs;
        private set {
            if (_isLoadingJobs == value) return;
            _isLoadingJobs = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoadingJobs)));
        }
    }

    public string SelectedFilePath {
        get => _selectedFilePath;
        private set {
            if (string.Equals(_selectedFilePath, value, StringComparison.Ordinal)) return;
            _selectedFilePath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedFilePath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedFile)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartGraphJob)));
        }
    }

    public string StatusMessage {
        get => _statusMessage;
        private set {
            if (string.Equals(_statusMessage, value, StringComparison.Ordinal)) return;
            _statusMessage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasStatusMessage)));
        }
    }

    public bool HasSelectedFile => !string.IsNullOrWhiteSpace(SelectedFilePath);

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool CanSelectFile => !IsStartingGraphJob;

    public bool CanStartGraphJob =>
        !IsStartingGraphJob && !string.IsNullOrWhiteSpace(SelectedFilePath);

    public ICommand StartGraphJobCommand { get; }
    public ICommand RefreshJobsCommand { get; }
    public ICommand ImportGraphJobCommand { get; }

    internal void ApplySelectedFile(string filePath) {
        SelectedFilePath = filePath;
        StatusMessage = $"Selected CSV: {Path.GetFileName(filePath)}";
    }

    internal async Task<bool> BeginFileSelectionAsync() {
        if (IsStartingGraphJob) return false;
        return true;
    }

    internal void EndFileSelection() { }

    internal async Task InitializeAsync() {
        await LoadJobsAsync(forceRefresh: true);
    }

    private async Task LoadJobsAsync(bool forceRefresh) {
        if (!await _loadJobsSemaphore.WaitAsync(0).ConfigureAwait(false)) return;

        try {
            await RunOnUiThreadAsync(() => IsLoadingJobs = true).ConfigureAwait(false);

            var jobs = await RunOffUiThreadAsync(
                () => _localProcessorService.GetJobsAsync(default)).ConfigureAwait(false);

            // Filter for bike-graph jobs only (graph seeding)
            var graphJobs = jobs
                .Where(j => string.Equals(j.DocumentType, "bike-graph", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(j => j.CreatedAtUtc)
                .ThenBy(j => j.JobId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await RunOnUiThreadAsync(() => {
                LocalProcessorJobs.Clear();
                foreach (var job in graphJobs) {
                    LocalProcessorJobs.Add(new LocalProcessorJobViewModel {
                        JobId = job.JobId,
                        UploadId = job.UploadId,
                        DocumentType = job.DocumentType,
                        Status = job.Status,
                        Message = job.Message,
                        Progress = job.Progress,
                        CreatedAtUtc = job.CreatedAtUtc,
                        UpdatedAtUtc = job.UpdatedAtUtc,
                        NodesCreated = job.NodesCreated,
                        EdgesCreated = job.EdgesCreated
                    });
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Failed to load local processor graph jobs.");
        }
        finally {
            await RunOnUiThreadAsync(() => IsLoadingJobs = false).ConfigureAwait(false);
            _loadJobsSemaphore.Release();
        }
    }

    private async Task ImportGraphJobAsync(LocalProcessorJobViewModel? job) {
        if (job is null || job.IsImporting) return;

        var authorized = await EnsureAuthorizedAsync(showErrors: true).ConfigureAwait(false);
        if (!authorized) return;

        await RunOnUiThreadAsync(() => job.IsImporting = true).ConfigureAwait(false);
        try {
            var request = new GraphImportStartRequest { UploadId = job.UploadId };
            var result = await RunOffUiThreadAsync(
                () => _apiClient.ImportGraphArtifactsAsync(request, default)).ConfigureAwait(false);

            await RunOnUiThreadAsync(() => {
                job.GraphImportStatus = result.Status;
                job.GraphImportFailureReason = result.FailureReason;
                StatusMessage = $"Imported graph artifact for upload {job.UploadId}.";
            }).ConfigureAwait(false);
            await LoadJobsAsync(forceRefresh: true);
        }
        catch (Exception ex) {
            await ErrorPresenter.ShowErrorAsync(
                "Import Failed",
                ErrorPresenter.SanitizeErrorMessage(ex.Message)).ConfigureAwait(false);
            _logger.LogError(ex, "Failed to import graph artifact for upload {UploadId}", job.UploadId);
        }
        finally {
            await RunOnUiThreadAsync(() => job.IsImporting = false).ConfigureAwait(false);
        }
    }

    private async Task StartGraphJobAsync() {
        if (IsStartingGraphJob || string.IsNullOrWhiteSpace(SelectedFilePath)) return;

        var selectedPath = SelectedFilePath;
        var fileName = Path.GetFileName(selectedPath);

        try {
            await RunOffUiThreadAsync(() => ValidateFile(selectedPath)).ConfigureAwait(false);
        }
        catch (Exception ex) {
            await ErrorPresenter.ShowErrorAsync(
                "Invalid CSV",
                ErrorPresenter.SanitizeErrorMessage(ex.Message)).ConfigureAwait(false);
            return;
        }

        await RunOnUiThreadAsync(() => IsStartingGraphJob = true).ConfigureAwait(false);

        try {
            var health = await RunOffUiThreadAsync(
                () => _localProcessorService.GetHealthAsync()).ConfigureAwait(false);
            if (health is null) {
                await RunOnUiThreadAsync(() => StatusMessage = "Starting local processor...").ConfigureAwait(false);
                await RunOffUiThreadAsync(() => _localProcessorService.StartAsync()).ConfigureAwait(false);
            }

            await RunOnUiThreadAsync(() => StatusMessage = $"Starting graph job for {fileName}...").ConfigureAwait(false);
            var result = await RunOffUiThreadAsync(
                () => _localProcessorService.StartBikeGraphJobAsync(selectedPath)).ConfigureAwait(false);

            await RunOnUiThreadAsync(() => {
                SelectedFilePath = string.Empty;
                StatusMessage = $"Graph job {result.JobId} started for {fileName}. Jobs list will update on next refresh.";
            }).ConfigureAwait(false);
            await LoadJobsAsync(forceRefresh: true);
        }
        catch (Exception ex) {
            await RunOnUiThreadAsync(() => StatusMessage = "Failed to start the graph job.").ConfigureAwait(false);
            await ErrorPresenter.ShowErrorAsync(
                "Graph Job Failed",
                ErrorPresenter.SanitizeErrorMessage(ex.Message)).ConfigureAwait(false);
            _logger.LogError(ex, "Failed to start graph job for {FileName}", fileName);
        }
        finally {
            await RunOnUiThreadAsync(() => IsStartingGraphJob = false).ConfigureAwait(false);
        }
    }

    private async Task<bool> EnsureAuthorizedAsync(bool showErrors) {
        if (!_configService.IsApiConfigured) {
            if (showErrors) {
                await ErrorPresenter.ShowWarningAsync(
                    "Configuration Required",
                    "API is not configured. Go to Settings to configure the API base URL.");
            }
            return false;
        }

        if (!_authService.IsSignedIn()) {
            if (showErrors) {
                await ErrorPresenter.ShowWarningAsync(
                    "Sign In Required", "Please sign in to access API-backed actions.");
            }
            return false;
        }

        var isAuthorized = await RunOffUiThreadAsync(
            () => _authService.IsAuthorizedAdminAsync()).ConfigureAwait(false);
        if (!isAuthorized) {
            if (showErrors) {
                await ErrorPresenter.ShowWarningAsync(
                    "Access Denied", "You do not have permission to perform this action.");
            }
            return false;
        }

        return true;
    }

    private static void ValidateFile(string filePath) {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new InvalidOperationException("Choose a CSV file before starting a graph import.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException("The selected CSV file no longer exists.", filePath);

        if (!string.Equals(Path.GetExtension(filePath), ".csv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only CSV files can be used for graph import.");

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
            throw new InvalidOperationException("The selected CSV file is empty.");

        if (fileInfo.Length > MaxFileSizeBytes)
            throw new InvalidOperationException("The selected CSV exceeds the 2 GB graph import limit.");
    }

    private static Task RunOnUiThreadAsync(Action action) => MauiThreading.RunOnMainThreadAsync(action);

    private static Task RunOnUiThreadAsync(Func<Task> action) => MauiThreading.RunOnMainThreadAsync(action);

    private static Task RunOffUiThreadAsync(Action action, CancellationToken cancellationToken = default) =>
        MauiThreading.RunOffMainThreadAsync(action, cancellationToken);

    private static Task RunOffUiThreadAsync(Func<Task> action, CancellationToken cancellationToken = default) =>
        MauiThreading.RunOffMainThreadAsync(action, cancellationToken);

    private static Task<T> RunOffUiThreadAsync<T>(Func<T> action, CancellationToken cancellationToken = default) =>
        MauiThreading.RunOffMainThreadAsync(action, cancellationToken);

    private static Task<T> RunOffUiThreadAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default) =>
        MauiThreading.RunOffMainThreadAsync(action, cancellationToken);

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (disposing) {
            _loadJobsSemaphore.Dispose();
        }
    }

    ~GraphSeedingViewModel() {
        Dispose(false);
    }
}