using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Models.Api;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Utilities;
using MotorcycleRAG.Contracts.Models.DTOs;
using Polly.CircuitBreaker;
using Polly.Timeout;
using LocalProcessorHealthResponse = MotorcycleRAG.Admin.Services.Dtos.LocalProcessorHealthResponse;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for the pipeline jobs page.
/// Handles loading pending storage files, ingestion job history, and legacy execution history.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "Internal patterns")]
internal class JobsViewModel : IDisposable, INotifyPropertyChanged {
    private const int RecentIngestionJobCount = 50;
    private const int MaxAutomaticApiSectionFailures = 5;
    private const string PendingStorageFilesSectionName = "Pending storage files";
    private const string IngestionJobsSectionName = "Ingestion jobs";
    private const string PipelineExecutionsSectionName = "Pipeline executions";
    private readonly ApiClient _apiClient;
    private readonly IAdminAuthService _authService;
    private readonly IConfigurationStateService _configService;
    private readonly ILocalProcessorService _localProcessorService;
    private readonly ILogger<JobsViewModel> _logger;
    private readonly SemaphoreSlim _loadJobsSemaphore = new(1, 1);
    private readonly object _automaticApiPollingSync = new();
    private readonly object _loadJobsCancellationSync = new();
    private bool _isLoading;
    private bool _isStartingLocalProcessor;
    private bool _isStoppingLocalProcessor;
    private bool _isClearingLocalProcessorJobs;
    private bool _isLocalProcessorRunning;
    private bool _isLocalProcessorAcceptingWork;
    private bool _autoPollIngestionJobsEnabled = true;
    private bool _autoPollPendingStorageFilesEnabled = true;
    private bool _autoPollPipelineExecutionsEnabled = true;
    private int _pendingStorageFilesFailureCount;
    private int _ingestionJobsFailureCount;
    private int _pipelineExecutionsFailureCount;
    private int _localProcessorActiveJobs;
    private string _localProcessorStatusMessage = "Local processor is not running.";
    private string _localProcessorDiagnosticOutput = string.Empty;
    private string _apiPollingProblemMessage = string.Empty;
    private CancellationTokenSource? _loadJobsCancellationTokenSource;

    public JobsViewModel(
        ApiClient apiClient,
        IAdminAuthService authService,
        IConfigurationStateService configService,
        ILocalProcessorService localProcessorService,
        ILogger<JobsViewModel> logger) {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _localProcessorService = localProcessorService ?? throw new ArgumentNullException(nameof(localProcessorService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        PendingStorageFiles = new ObservableCollection<PendingStorageFileViewModel>();
        IngestionJobs = new ObservableCollection<IngestionJobHistoryViewModel>();
        Jobs = new ObservableCollection<JobViewModel>();
        LocalProcessorJobs = new ObservableCollection<LocalProcessorJobViewModel>();
        LocalProcessorJobs.CollectionChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanClearLocalProcessorJobs)));

        LoadJobsCommand = new AsyncRelayCommand(() => LoadJobsAsync(forceApiRetry: true, showBusyIndicator: true));
        CancelJobCommand = new AsyncRelayCommand<string>(CancelJobAsync);
        ProcessPendingStorageFileCommand = new AsyncRelayCommand<PendingStorageFileViewModel>(ProcessPendingStorageFileAsync);
        ProcessPendingStorageFileAsGraphCommand = new AsyncRelayCommand<PendingStorageFileViewModel>(
            pendingFile => ProcessPendingStorageFileAsync(pendingFile, "bike-graph"));
        StartLocalProcessorCommand = new AsyncRelayCommand(StartLocalProcessorAsync);
        StopLocalProcessorCommand = new AsyncRelayCommand(StopLocalProcessorAsync);
        ClearLocalProcessorJobsCommand = new AsyncRelayCommand(ClearLocalProcessorJobsAsync);
        ImportLocalProcessorJobCommand = new AsyncRelayCommand<LocalProcessorJobViewModel>(ImportLocalProcessorJobAsync);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PendingStorageFileViewModel> PendingStorageFiles { get; }

    public ObservableCollection<IngestionJobHistoryViewModel> IngestionJobs { get; }

    public ObservableCollection<JobViewModel> Jobs { get; }

    public ObservableCollection<LocalProcessorJobViewModel> LocalProcessorJobs { get; }

    public bool IsLoading {
        get => _isLoading;
        set {
            if (_isLoading != value) {
                _isLoading = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartLocalProcessor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStopLocalProcessor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanClearLocalProcessorJobs)));
            }
        }
    }

    public bool IsStartingLocalProcessor {
        get => _isStartingLocalProcessor;
        private set {
            if (_isStartingLocalProcessor == value) {
                return;
            }

            _isStartingLocalProcessor = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStartingLocalProcessor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartLocalProcessor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStopLocalProcessor)));
        }
    }

    public bool IsStoppingLocalProcessor {
        get => _isStoppingLocalProcessor;
        private set {
            if (_isStoppingLocalProcessor == value) {
                return;
            }

            _isStoppingLocalProcessor = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStoppingLocalProcessor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartLocalProcessor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStopLocalProcessor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanClearLocalProcessorJobs)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalProcessorStateLabel)));
        }
    }

    public bool IsClearingLocalProcessorJobs {
        get => _isClearingLocalProcessorJobs;
        private set {
            if (_isClearingLocalProcessorJobs == value) {
                return;
            }

            _isClearingLocalProcessorJobs = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsClearingLocalProcessorJobs)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanClearLocalProcessorJobs)));
        }
    }

    public bool IsLocalProcessorRunning {
        get => _isLocalProcessorRunning;
        private set {
            if (_isLocalProcessorRunning == value) {
                return;
            }

            _isLocalProcessorRunning = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLocalProcessorRunning)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartLocalProcessor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStopLocalProcessor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanClearLocalProcessorJobs)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalProcessorStateLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowLocalProcessorDiagnosticPanel)));
        }
    }

    public bool IsLocalProcessorAcceptingWork {
        get => _isLocalProcessorAcceptingWork;
        private set {
            if (_isLocalProcessorAcceptingWork == value) {
                return;
            }

            _isLocalProcessorAcceptingWork = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLocalProcessorAcceptingWork)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalProcessorStateLabel)));
        }
    }

    public int LocalProcessorActiveJobs {
        get => _localProcessorActiveJobs;
        private set {
            if (_localProcessorActiveJobs == value) {
                return;
            }

            _localProcessorActiveJobs = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalProcessorActiveJobs)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalProcessorStateLabel)));
        }
    }

    public string LocalProcessorStatusMessage {
        get => _localProcessorStatusMessage;
        private set {
            if (string.Equals(_localProcessorStatusMessage, value, StringComparison.Ordinal)) {
                return;
            }

            _localProcessorStatusMessage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalProcessorStatusMessage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalProcessorStatusIsError)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowLocalProcessorDiagnosticPanel)));
        }
    }

    /// <summary>True when the status message represents a failure — drives the red label color in the UI.</summary>
    public bool LocalProcessorStatusIsError =>
        _localProcessorStatusMessage.StartsWith("Failed", StringComparison.OrdinalIgnoreCase);

    public string LocalProcessorDiagnosticOutput {
        get => _localProcessorDiagnosticOutput;
        private set {
            if (string.Equals(_localProcessorDiagnosticOutput, value, StringComparison.Ordinal)) {
                return;
            }

            _localProcessorDiagnosticOutput = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalProcessorDiagnosticOutput)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLocalProcessorDiagnosticOutput)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowLocalProcessorDiagnosticPanel)));
        }
    }

    public bool ShowLocalProcessorDiagnosticPanel =>
        IsLocalProcessorRunning || HasLocalProcessorDiagnosticOutput;

    public string ApiPollingProblemMessage {
        get => _apiPollingProblemMessage;
        private set {
            if (string.Equals(_apiPollingProblemMessage, value, StringComparison.Ordinal)) {
                return;
            }

            _apiPollingProblemMessage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApiPollingProblemMessage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasApiPollingProblemMessage)));
        }
    }

    public string LocalProcessorStateLabel =>
        !IsLocalProcessorRunning
            ? "Stopped"
            : IsStoppingLocalProcessor || !IsLocalProcessorAcceptingWork
                ? $"Stopping ({LocalProcessorActiveJobs} active jobs)"
                : $"Running ({LocalProcessorActiveJobs} active jobs)";

    public bool HasLocalProcessorDiagnosticOutput => !string.IsNullOrWhiteSpace(LocalProcessorDiagnosticOutput);

    public bool HasApiPollingProblemMessage => !string.IsNullOrWhiteSpace(ApiPollingProblemMessage);

    public bool CanStartLocalProcessor =>
        !IsLoading &&
        !IsStartingLocalProcessor &&
        !IsStoppingLocalProcessor &&
        !IsLocalProcessorRunning &&
        _configService.IsLocalProcessorConfigured;

    public bool CanStopLocalProcessor =>
        !IsLoading &&
        !IsStartingLocalProcessor &&
        !IsStoppingLocalProcessor &&
        IsLocalProcessorRunning;

    public bool CanClearLocalProcessorJobs =>
        !IsLoading &&
        !IsClearingLocalProcessorJobs &&
        !IsStoppingLocalProcessor &&
        IsLocalProcessorRunning &&
        LocalProcessorJobs.Any(job => !job.IsRunning);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand LoadJobsCommand { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand CancelJobCommand { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand ProcessPendingStorageFileCommand { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand ProcessPendingStorageFileAsGraphCommand { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand StartLocalProcessorCommand { get; }

    public ICommand StopLocalProcessorCommand { get; }

    public ICommand ClearLocalProcessorJobsCommand { get; }

    public ICommand ImportLocalProcessorJobCommand { get; }

    public async Task InitializeAsync() {
        await LoadJobsAsync(forceApiRetry: true, showBusyIndicator: true);
    }

    internal Task RefreshAsync() => LoadJobsAsync(forceApiRetry: false, showBusyIndicator: false);

    private async Task LoadJobsAsync(bool forceApiRetry, bool showBusyIndicator = true) {
        if (!await _loadJobsSemaphore.WaitAsync(0).ConfigureAwait(false)) {
            return;
        }

        var loadJobsCancellationTokenSource = RegisterLoadJobsCancellationTokenSource();
        var cancellationToken = loadJobsCancellationTokenSource.Token;
        if (showBusyIndicator) {
            await RunOnUiThreadAsync(() => IsLoading = true).ConfigureAwait(false);
        }
        try {
            var loadResult = await RunOffUiThreadAsync(
                    () => LoadJobsCoreAsync(forceApiRetry, cancellationToken))
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            await RunOnUiThreadAsync(() => ApplyLoadJobsResult(
                    loadResult.LocalHealth,
                    loadResult.LocalJobs,
                    loadResult.Executions,
                    loadResult.PendingFiles,
                    loadResult.IngestionJobs,
                    loadResult.LoadedPipelineExecutions,
                    loadResult.LoadedPendingStorageFiles,
                    loadResult.LoadedIngestionJobs))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            _logger.LogDebug("Jobs refresh was cancelled.");
        }
        finally {
            if (showBusyIndicator) {
                await RunOnUiThreadAsync(() => IsLoading = false).ConfigureAwait(false);
            }
            ClearLoadJobsCancellationTokenSource(loadJobsCancellationTokenSource);
            _loadJobsSemaphore.Release();
        }
    }

    private async Task<(
        LocalProcessorHealthResponse? LocalHealth,
        List<LocalProcessorJobViewModel> LocalJobs,
        List<PipelineExecution> Executions,
        List<PendingStorageFileDto> PendingFiles,
        List<IngestionJobStatusResponse> IngestionJobs,
        bool LoadedPipelineExecutions,
        bool LoadedPendingStorageFiles,
        bool LoadedIngestionJobs)> LoadJobsCoreAsync(bool forceApiRetry, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (forceApiRetry) {
            await ResetAutomaticApiSectionPollingAsync().ConfigureAwait(false);
        }

        var localHealth = await _localProcessorService.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        var localJobsTask = localHealth is null
            ? Task.FromResult(new List<LocalProcessorJobViewModel>())
            : TryGetLocalProcessorJobsAsync(cancellationToken);

        List<PipelineExecution> executions = [];
        List<PendingStorageFileDto> pendingFiles = [];
        List<IngestionJobStatusResponse> ingestionJobs = [];
        var loadedPipelineExecutions = false;
        var loadedPendingStorageFiles = false;
        var loadedIngestionJobs = false;

        if (CanLoadApiData()) {
            loadedPipelineExecutions = forceApiRetry || _autoPollPipelineExecutionsEnabled;
            loadedPendingStorageFiles = forceApiRetry || _autoPollPendingStorageFilesEnabled;
            loadedIngestionJobs = forceApiRetry || _autoPollIngestionJobsEnabled;

            var executionsTask = loadedPipelineExecutions
                ? TryGetPipelineExecutionsAsync(cancellationToken)
                : Task.FromResult(new List<PipelineExecution>());
            var pendingFilesTask = loadedPendingStorageFiles
                ? TryGetPendingStorageFilesAsync(cancellationToken)
                : Task.FromResult(new List<PendingStorageFileDto>());
            var ingestionJobsTask = loadedIngestionJobs
                ? TryGetIngestionJobsAsync(cancellationToken)
                : Task.FromResult(new List<IngestionJobStatusResponse>());
            await Task.WhenAll(localJobsTask, executionsTask, pendingFilesTask, ingestionJobsTask).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            executions = await executionsTask.ConfigureAwait(false);
            pendingFiles = await pendingFilesTask.ConfigureAwait(false);
            ingestionJobs = await ingestionJobsTask.ConfigureAwait(false);
        }
        else {
            await localJobsTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var localJobs = await localJobsTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return (
            localHealth,
            localJobs,
            executions,
            pendingFiles,
            ingestionJobs,
            loadedPipelineExecutions,
            loadedPendingStorageFiles,
            loadedIngestionJobs);
    }

    private void ApplyLoadJobsResult(
        LocalProcessorHealthResponse? localHealth,
        List<LocalProcessorJobViewModel> localJobs,
        List<PipelineExecution> executions,
        List<PendingStorageFileDto> pendingFiles,
        List<IngestionJobStatusResponse> ingestionJobs,
        bool loadedPipelineExecutions,
        bool loadedPendingStorageFiles,
        bool loadedIngestionJobs) {
        var latestGraphImportsByUpload = loadedIngestionJobs
            ? ingestionJobs
                .Where(job => string.Equals(job.InputType, "BikeGraph", StringComparison.OrdinalIgnoreCase)
                              && !string.IsNullOrWhiteSpace(job.InputRef))
                .GroupBy(job => job.InputRef, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => {
                        var latestJob = group.OrderByDescending(job => job.CreatedAtUtc).First();
                        return (latestJob.Status, latestJob.FailureReason);
                    },
                    StringComparer.OrdinalIgnoreCase)
            : IngestionJobs
                .Where(job => string.Equals(job.InputType, "BikeGraph", StringComparison.OrdinalIgnoreCase)
                              && !string.IsNullOrWhiteSpace(job.InputRef))
                .GroupBy(job => job.InputRef, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => {
                        var latestJob = group.OrderByDescending(job => job.CreatedAtUtc).First();
                        return (latestJob.Status, latestJob.FailureReason);
                    },
                    StringComparer.OrdinalIgnoreCase);

        ApplyLocalProcessorState(localHealth);

        LocalProcessorJobs.Clear();
        foreach (var localJob in localJobs) {
            if (latestGraphImportsByUpload.TryGetValue(localJob.UploadId, out var graphImport)) {
                localJob.GraphImportStatus = graphImport.Status;
                localJob.GraphImportFailureReason = graphImport.FailureReason;
            }

            LocalProcessorJobs.Add(localJob);
        }

        if (loadedPendingStorageFiles) {
            PendingStorageFiles.Clear();
            foreach (var pendingFile in pendingFiles
                         .OrderByDescending(file => file.LastModifiedUtc)
                         .ThenBy(file => file.BlobName, StringComparer.OrdinalIgnoreCase)) {
                PendingStorageFiles.Add(new PendingStorageFileViewModel {
                    UploadId = pendingFile.UploadId,
                    BlobName = pendingFile.BlobName,
                    DocumentType = pendingFile.DocumentType,
                    SizeBytes = pendingFile.SizeBytes,
                    LastModifiedUtc = pendingFile.LastModifiedUtc,
                    LastKnownJobStatus = pendingFile.LastKnownJobStatus,
                    FailureReason = pendingFile.FailureReason,
                    GraphImportStatus = pendingFile.GraphImportStatus,
                    GraphImportFailureReason = pendingFile.GraphImportFailureReason
                });
            }
        }

        if (loadedIngestionJobs) {
            IngestionJobs.Clear();
            foreach (var ingestionJob in ingestionJobs
                         .OrderByDescending(job => job.CreatedAtUtc)
                         .ThenBy(job => job.JobId)) {
                IngestionJobs.Add(new IngestionJobHistoryViewModel {
                    JobId = ingestionJob.JobId,
                    Status = ingestionJob.Status,
                    CreatedAtUtc = ingestionJob.CreatedAtUtc,
                    StartedAtUtc = ingestionJob.StartedAtUtc,
                    CompletedAtUtc = ingestionJob.CompletedAtUtc,
                    InputType = ingestionJob.InputType,
                    InputRef = ingestionJob.InputRef,
                    FailureReason = ingestionJob.FailureReason,
                    FabricRunId = ingestionJob.FabricRunId
                });
            }
        }

        if (loadedPipelineExecutions) {
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
                    Warnings = new ObservableCollection<string>(execution.Warnings)
                });
            }
        }
    }

    private async Task RefreshLocalProcessorSectionAsync(LocalProcessorHealthResponse? knownHealth = null) {
        await _loadJobsSemaphore.WaitAsync().ConfigureAwait(false);
        try {
            var refreshResult = await RunOffUiThreadAsync(
                    () => RefreshLocalProcessorSectionCoreAsync(knownHealth))
                .ConfigureAwait(false);

            await RunOnUiThreadAsync(() => {
                ApplyLocalProcessorState(refreshResult.Health);
                LocalProcessorJobs.Clear();
                foreach (var localJob in refreshResult.LocalJobs) {
                    LocalProcessorJobs.Add(localJob);
                }
            }).ConfigureAwait(false);
        }
        finally {
            _loadJobsSemaphore.Release();
        }
    }

    private async Task<(LocalProcessorHealthResponse? Health, List<LocalProcessorJobViewModel> LocalJobs)> RefreshLocalProcessorSectionCoreAsync(
        LocalProcessorHealthResponse? knownHealth) {
        var health = knownHealth ?? await _localProcessorService.GetHealthAsync(default).ConfigureAwait(false);
        var localJobs = health is null
            ? []
            : await TryGetLocalProcessorJobsAsync(default).ConfigureAwait(false);
        return (health, localJobs);
    }

    private async Task CancelJobAsync(string? executionId) {
        if (string.IsNullOrWhiteSpace(executionId)) {
            return;
        }

        try {
            var confirm = await ErrorPresenter.ShowConfirmAsync(
                "Cancel Job",
                $"Are you sure you want to cancel execution {executionId}?",
                "Yes",
                "No").ConfigureAwait(false);

            if (!confirm) {
                return;
            }

            var result = await RunOffUiThreadAsync(() => _apiClient.CancelPipelineAsync(executionId)).ConfigureAwait(false);
            if (result.Cancelled) {
                await ErrorPresenter.ShowSuccessAsync("Success", "Job cancelled successfully").ConfigureAwait(false);
                await LoadJobsAsync(forceApiRetry: true).ConfigureAwait(false);
            }
            else {
                await ErrorPresenter.ShowErrorAsync("Error", "Failed to cancel job").ConfigureAwait(false);
            }
        }
        catch (UnauthorizedAccessException ex) {
            _logger.LogWarning(ex, "User not authorized to cancel job {ExecutionId}", executionId);
        }
        catch (HttpRequestException ex) {
            await ErrorPresenter.ShowErrorAsync("Error", "Failed to reach the API server").ConfigureAwait(false);

            _logger.LogWarning(ex, "Failed to reach API server when canceling execution {ExecutionId}", executionId);
        }
        catch (OperationCanceledException ex) {
            _logger.LogWarning(ex, "Cancel job operation timed out for execution {ExecutionId}", executionId);
        }
    }

    private async Task ProcessPendingStorageFileAsync(PendingStorageFileViewModel? pendingFile) {
        await ProcessPendingStorageFileAsync(pendingFile, pendingFile?.DocumentType ?? string.Empty);
    }

    private async Task StartLocalProcessorAsync() {
        await RunOnUiThreadAsync(() => {
            IsStartingLocalProcessor = true;
            LocalProcessorStatusMessage = "Starting local processor...";
        }).ConfigureAwait(false);

        try {
            var health = await RunOffUiThreadAsync(() => _localProcessorService.StartAsync()).ConfigureAwait(false);
            await RunOnUiThreadAsync(() => LocalProcessorDiagnosticOutput = string.Empty).ConfigureAwait(false);
            await RefreshLocalProcessorSectionAsync(health).ConfigureAwait(false);
        }
        catch (Exception ex) {
            // Populate the diagnostic panel first — it shows the full unsanitized message including
            // "Recent output: ..." from the process, which is the most actionable debugging info.
            var diagnostic = ex.Message;
            await RunOnUiThreadAsync(() => {
                LocalProcessorStatusMessage = "Failed to start. See output below.";
                LocalProcessorDiagnosticOutput = diagnostic;
            }).ConfigureAwait(false);

            // Show a concise dialog — just enough to draw attention; details are on the page.
            var headline = ex is TimeoutException
                ? "The local processor did not start within the timeout. See the 'Recent Process Output' panel below for details."
                : ex is InvalidOperationException
                    ? ex.Message.Split('.')[0] + ". See the 'Recent Process Output' panel below."
                    : "An unexpected error prevented the local processor from starting. See the 'Recent Process Output' panel below.";
            await ErrorPresenter.ShowErrorAsync("Local Processor Failed", headline).ConfigureAwait(false);
            _logger.LogError(ex, "Failed to start the local processor.");
        }
        finally {
            await RunOnUiThreadAsync(() => IsStartingLocalProcessor = false).ConfigureAwait(false);
        }
    }

    private async Task StopLocalProcessorAsync() {
        var confirm = await ErrorPresenter.ShowConfirmAsync(
            "Stop Local Processor",
            "Stop the local processor gracefully after active jobs finish?",
            "Stop",
            "Cancel").ConfigureAwait(false);

        if (!confirm) {
            return;
        }

        await RunOnUiThreadAsync(() => {
            IsStoppingLocalProcessor = true;
            LocalProcessorStatusMessage = "Stopping local processor gracefully...";
        }).ConfigureAwait(false);

        try {
            await RunOffUiThreadAsync(() => _localProcessorService.StopAsync()).ConfigureAwait(false);
            await RefreshLocalProcessorSectionAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            await RunOnUiThreadAsync(() => LocalProcessorStatusMessage = "The local processor did not stop cleanly.").ConfigureAwait(false);
            await ErrorPresenter.ShowErrorAsync(
                "Stop Failed",
                ErrorPresenter.SanitizeErrorMessage(ex.Message)).ConfigureAwait(false);
            _logger.LogError(ex, "Failed to stop the local processor.");
        }
        finally {
            await RunOnUiThreadAsync(() => IsStoppingLocalProcessor = false).ConfigureAwait(false);
        }
    }

    private async Task ClearLocalProcessorJobsAsync() {
        var clearableJobCount = LocalProcessorJobs.Count(job => !job.IsRunning);
        if (clearableJobCount == 0) {
            return;
        }

        var confirm = await ErrorPresenter.ShowConfirmAsync(
            "Clear Finished Local Jobs",
            clearableJobCount == 1
                ? "Remove the finished local processor job from this list? Active jobs will be kept."
                : $"Remove {clearableJobCount} finished local processor jobs from this list? Active jobs will be kept.",
            "Clear Finished",
            "Cancel").ConfigureAwait(false);

        if (!confirm) {
            return;
        }

        await RunOnUiThreadAsync(() => IsClearingLocalProcessorJobs = true).ConfigureAwait(false);

        try {
            _ = await RunOffUiThreadAsync(() => _localProcessorService.ClearFinishedJobsAsync()).ConfigureAwait(false);
            await RefreshLocalProcessorSectionAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            await ErrorPresenter.ShowErrorAsync(
                "Cleanup Failed",
                ErrorPresenter.SanitizeErrorMessage(ex.Message)).ConfigureAwait(false);
            _logger.LogError(ex, "Failed to clear finished local processor jobs.");
        }
        finally {
            await RunOnUiThreadAsync(() => IsClearingLocalProcessorJobs = false).ConfigureAwait(false);
        }
    }

    private async Task ImportLocalProcessorJobAsync(LocalProcessorJobViewModel? job) {
        if (job is null || job.IsImporting) {
            return;
        }

        var authorized = await EnsureAuthorizedAsync(showErrors: true).ConfigureAwait(false);
        if (!authorized) {
            return;
        }

        await RunOnUiThreadAsync(() => job.IsImporting = true).ConfigureAwait(false);
        try {
            var request = new GraphImportStartRequest { UploadId = job.UploadId };
            var result = await RunOffUiThreadAsync(() => _apiClient.ImportGraphArtifactsAsync(request, default)).ConfigureAwait(false);

            await RunOnUiThreadAsync(() => {
                job.GraphImportStatus = result.Status;
                job.GraphImportFailureReason = result.FailureReason;
            }).ConfigureAwait(false);
            await LoadJobsAsync(forceApiRetry: true).ConfigureAwait(false);
        }
        catch (Exception ex) {
            await ErrorPresenter.ShowErrorAsync(
                "Import Failed",
                ErrorPresenter.SanitizeErrorMessage(ex.Message)).ConfigureAwait(false);
            _logger.LogError(ex, "Failed to import processed graph artifact for upload {UploadId}", job.UploadId);
        }
        finally {
            await RunOnUiThreadAsync(() => job.IsImporting = false).ConfigureAwait(false);
        }
    }

    private async Task ProcessPendingStorageFileAsync(PendingStorageFileViewModel? pendingFile, string workflowDocumentType) {
        if (pendingFile is null || pendingFile.IsProcessing) {
            return;
        }

        var authorized = await EnsureAuthorizedAsync(showErrors: true).ConfigureAwait(false);
        if (!authorized) {
            return;
        }

        await RunOnUiThreadAsync(() => pendingFile.IsProcessing = true).ConfigureAwait(false);
        try {
            var request = new IngestionJobStartRequest {
                UploadId = pendingFile.UploadId,
                DocumentType = workflowDocumentType,
                Configuration = CreateDefaultIngestionConfiguration(workflowDocumentType)
            };

            var result = await RunOffUiThreadAsync(() => _apiClient.StartIngestionJobAsync(request, default)).ConfigureAwait(false);
            var isGraphImport = string.Equals(workflowDocumentType, "bike-graph", StringComparison.OrdinalIgnoreCase);
            var title = isGraphImport ? "Graph Import Queued" : "Ingestion Queued";
            var message = isGraphImport
                ? $"Queued graph import for {pendingFile.BlobName} with job {result.JobId}."
                : $"Queued {pendingFile.BlobName} with job {result.JobId}.";

            await ErrorPresenter.ShowSuccessAsync(title, message).ConfigureAwait(false);

            await LoadJobsAsync(forceApiRetry: true).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex) {
            _logger.LogWarning(ex, "User not authorized to process pending storage file {UploadId} for workflow {WorkflowDocumentType}", pendingFile.UploadId, workflowDocumentType);
        }
        catch (HttpRequestException ex) {
            var isGraphImport = string.Equals(workflowDocumentType, "bike-graph", StringComparison.OrdinalIgnoreCase);
            var message = isGraphImport
                ? "Failed to queue the graph import job."
                : "Failed to queue the ingestion job.";
            if (ex.StatusCode == HttpStatusCode.BadRequest) {
                message = isGraphImport
                    ? "The API rejected the graph import request. Check the upload ID and current processing mode."
                    : "The API rejected the ingestion request. Check the file type and current ingestion configuration.";
            }

            await ErrorPresenter.ShowErrorAsync("Queue Failed", message);
            _logger.LogWarning(ex, "Failed to queue pending storage file {UploadId} for workflow {WorkflowDocumentType}", pendingFile.UploadId, workflowDocumentType);
        }
        catch (OperationCanceledException ex) {
            _logger.LogWarning(ex, "Queue operation timed out for upload {UploadId} and workflow {WorkflowDocumentType}", pendingFile.UploadId, workflowDocumentType);
        }
        finally {
            await RunOnUiThreadAsync(() => pendingFile.IsProcessing = false).ConfigureAwait(false);
        }
    }

    private async Task<List<PendingStorageFileDto>> TryGetPendingStorageFilesAsync(CancellationToken cancellationToken) {
        try {
            var pendingFiles = await _apiClient.GetPendingStorageFilesAsync(cancellationToken).ConfigureAwait(false);
            ResetAutomaticApiSectionFailureCount(PendingStorageFilesSectionName);
            return pendingFiles;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
            await PauseAutomaticApiSectionPollingAsync(
                PendingStorageFilesSectionName,
                ex.StatusCode,
                "The endpoint is not available on the current API.")
                .ConfigureAwait(false);
            _logger.LogInformation(ex, "Pending storage files endpoint is not available on the current API.");
            return [];
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest) {
            await PauseAutomaticApiSectionPollingAsync(
                PendingStorageFilesSectionName,
                ex.StatusCode,
                "The API rejected the request.")
                .ConfigureAwait(false);
            _logger.LogInformation("Pending storage files request was rejected by the API.");
            return [];
        }
        catch (Exception ex) when (IsTransientApiFailure(ex)) {
            await RecordAutomaticApiSectionFailureAsync(PendingStorageFilesSectionName, ex).ConfigureAwait(false);
            return [];
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Pending storage files could not be loaded.");
            return [];
        }
    }

    private async Task<List<IngestionJobStatusResponse>> TryGetIngestionJobsAsync(CancellationToken cancellationToken) {
        try {
            var ingestionJobs = await _apiClient.GetIngestionJobsAsync(RecentIngestionJobCount, cancellationToken).ConfigureAwait(false);
            ResetAutomaticApiSectionFailureCount(IngestionJobsSectionName);
            return ingestionJobs;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
            await PauseAutomaticApiSectionPollingAsync(
                IngestionJobsSectionName,
                ex.StatusCode,
                "The endpoint is not available on the current API.")
                .ConfigureAwait(false);
            _logger.LogInformation(ex, "Ingestion jobs endpoint is not available on the current API.");
            return [];
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest) {
            await PauseAutomaticApiSectionPollingAsync(
                IngestionJobsSectionName,
                ex.StatusCode,
                "The API rejected the request.")
                .ConfigureAwait(false);
            _logger.LogInformation("Ingestion jobs request was rejected by the API.");
            return [];
        }
        catch (Exception ex) when (IsTransientApiFailure(ex)) {
            await RecordAutomaticApiSectionFailureAsync(IngestionJobsSectionName, ex).ConfigureAwait(false);
            return [];
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Ingestion jobs could not be loaded.");
            return [];
        }
    }

    private async Task<List<PipelineExecution>> TryGetPipelineExecutionsAsync(CancellationToken cancellationToken) {
        try {
            var pipelineExecutions = await _apiClient.GetPipelineExecutionsAsync(cancellationToken).ConfigureAwait(false);
            ResetAutomaticApiSectionFailureCount(PipelineExecutionsSectionName);
            return pipelineExecutions;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.BadRequest) {
            await PauseAutomaticApiSectionPollingAsync(
                PipelineExecutionsSectionName,
                ex.StatusCode,
                "The API rejected the request.")
                .ConfigureAwait(false);
            _logger.LogInformation("Pipeline executions request was rejected by the API.");
            return [];
        }
        catch (Exception ex) when (IsTransientApiFailure(ex)) {
            await RecordAutomaticApiSectionFailureAsync(PipelineExecutionsSectionName, ex).ConfigureAwait(false);
            return [];
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Pipeline executions could not be loaded.");
            return [];
        }
    }

    private async Task<List<LocalProcessorJobViewModel>> TryGetLocalProcessorJobsAsync(CancellationToken cancellationToken) {
        try {
            var jobs = await _localProcessorService.GetJobsAsync(cancellationToken);
            return jobs
                .OrderByDescending(job => job.CreatedAtUtc)
                .ThenBy(job => job.JobId, StringComparer.OrdinalIgnoreCase)
                .Select(job => new LocalProcessorJobViewModel {
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
                })
                .ToList();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (HttpRequestException) {
            _logger.LogDebug("Local processor jobs endpoint is not reachable.");
            return [];
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Local processor jobs could not be loaded.");
            return [];
        }
    }

    private async Task<bool> EnsureAuthorizedAsync(bool showErrors) {
        if (!_configService.IsApiConfigured) {
            _logger.LogWarning("Jobs page blocked: API not configured");
            if (showErrors) {
                await ErrorPresenter.ShowWarningAsync(
                    "Configuration Required",
                    "API is not configured. Go to Settings to configure the API base URL."
                );
            }
            return false;
        }

        if (!_authService.IsSignedIn()) {
            _logger.LogWarning("Jobs page blocked: user not signed in");
            if (showErrors) {
                await ErrorPresenter.ShowWarningAsync(
                    "Sign In Required",
                    "Please sign in to access API-backed pipeline actions."
                );
            }
            return false;
        }

        var isAuthorized = await RunOffUiThreadAsync(() => _authService.IsAuthorizedAdminAsync()).ConfigureAwait(false);
        if (!isAuthorized) {
            _logger.LogWarning("Jobs page blocked: user lacks admin permissions");
            if (showErrors) {
                await ErrorPresenter.ShowWarningAsync(
                    "Access Denied",
                    "You do not have permission to access API-backed pipeline actions."
                );
            }
            return false;
        }

        return true;
    }

    private bool CanLoadApiData() => _configService.IsApiConfigured && _authService.IsSignedIn();

    private async Task ResetAutomaticApiSectionPollingAsync() {
        lock (_automaticApiPollingSync) {
            _autoPollPendingStorageFilesEnabled = true;
            _autoPollIngestionJobsEnabled = true;
            _autoPollPipelineExecutionsEnabled = true;
            _pendingStorageFilesFailureCount = 0;
            _ingestionJobsFailureCount = 0;
            _pipelineExecutionsFailureCount = 0;
        }

        await RunOnUiThreadAsync(() => ApiPollingProblemMessage = string.Empty).ConfigureAwait(false);
    }

    private async Task PauseAutomaticApiSectionPollingAsync(
        string sectionName,
        HttpStatusCode? statusCode,
        string reason) {
        var shouldUpdateMessage = false;
        lock (_automaticApiPollingSync) {
            if (!IsAutomaticApiSectionPollingEnabled(sectionName)) {
                return;
            }

            SetAutomaticApiSectionPollingEnabled(sectionName, false);
            shouldUpdateMessage = true;
        }

        if (!shouldUpdateMessage) {
            return;
        }

        var problemMessage = $"{sectionName} automatic polling paused after the API returned {statusCode}. Use Refresh to retry.";
        _logger.LogWarning(
            "{SectionName} automatic polling paused after the API returned {StatusCode}. {Reason}",
            sectionName,
            statusCode,
            reason);
        await RunOnUiThreadAsync(() => ApiPollingProblemMessage = problemMessage).ConfigureAwait(false);
    }

    private void ResetAutomaticApiSectionFailureCount(string sectionName) {
        lock (_automaticApiPollingSync) {
            SetAutomaticApiSectionFailureCount(sectionName, 0);
        }
    }

    private async Task RecordAutomaticApiSectionFailureAsync(string sectionName, Exception exception) {
        var shouldPausePolling = false;
        var currentFailureCount = 0;
        lock (_automaticApiPollingSync) {
            if (!IsAutomaticApiSectionPollingEnabled(sectionName)) {
                return;
            }

            currentFailureCount = GetAutomaticApiSectionFailureCount(sectionName) + 1;
            SetAutomaticApiSectionFailureCount(sectionName, currentFailureCount);
            shouldPausePolling = currentFailureCount >= MaxAutomaticApiSectionFailures;
            if (shouldPausePolling) {
                SetAutomaticApiSectionPollingEnabled(sectionName, false);
            }
        }

        if (!shouldPausePolling) {
            return;
        }

        var reason = DescribeApiFailure(exception);
        var problemMessage = $"{sectionName} automatic polling paused after {currentFailureCount} consecutive failures. Use Refresh to retry.";
        _logger.LogWarning(
            exception,
            "{SectionName} automatic polling paused after {FailureCount} consecutive failures. Reason: {Reason}",
            sectionName,
            currentFailureCount,
            reason);
        await RunOnUiThreadAsync(() => ApiPollingProblemMessage = problemMessage).ConfigureAwait(false);
    }

    private bool IsAutomaticApiSectionPollingEnabled(string sectionName) {
        return sectionName switch {
            PendingStorageFilesSectionName => _autoPollPendingStorageFilesEnabled,
            IngestionJobsSectionName => _autoPollIngestionJobsEnabled,
            PipelineExecutionsSectionName => _autoPollPipelineExecutionsEnabled,
            _ => true
        };
    }

    private void SetAutomaticApiSectionPollingEnabled(string sectionName, bool isEnabled) {
        switch (sectionName) {
            case PendingStorageFilesSectionName:
                _autoPollPendingStorageFilesEnabled = isEnabled;
                break;
            case IngestionJobsSectionName:
                _autoPollIngestionJobsEnabled = isEnabled;
                break;
            case PipelineExecutionsSectionName:
                _autoPollPipelineExecutionsEnabled = isEnabled;
                break;
        }
    }

    private int GetAutomaticApiSectionFailureCount(string sectionName) {
        return sectionName switch {
            PendingStorageFilesSectionName => _pendingStorageFilesFailureCount,
            IngestionJobsSectionName => _ingestionJobsFailureCount,
            PipelineExecutionsSectionName => _pipelineExecutionsFailureCount,
            _ => 0
        };
    }

    private void SetAutomaticApiSectionFailureCount(string sectionName, int failureCount) {
        switch (sectionName) {
            case PendingStorageFilesSectionName:
                _pendingStorageFilesFailureCount = failureCount;
                break;
            case IngestionJobsSectionName:
                _ingestionJobsFailureCount = failureCount;
                break;
            case PipelineExecutionsSectionName:
                _pipelineExecutionsFailureCount = failureCount;
                break;
        }
    }

    private static bool IsTransientApiFailure(Exception exception) {
        if (exception is TimeoutRejectedException or BrokenCircuitException) {
            return true;
        }

        if (exception is HttpRequestException httpRequestException) {
            return httpRequestException.StatusCode is null ||
                   httpRequestException.StatusCode == HttpStatusCode.RequestTimeout ||
                   httpRequestException.StatusCode == HttpStatusCode.TooManyRequests ||
                   (int)httpRequestException.StatusCode >= 500;
        }

        return exception is TaskCanceledException;
    }

    private static string DescribeApiFailure(Exception exception) {
        return exception switch {
            TimeoutRejectedException => "The request timed out.",
            BrokenCircuitException => "The API is currently overloaded or unreachable. Please wait before retrying.",
            HttpRequestException httpRequestException when httpRequestException.StatusCode is not null =>
                $"The API returned {httpRequestException.StatusCode}.",
            HttpRequestException => "The API could not be reached.",
            TaskCanceledException => "The request was canceled before completion.",
            _ => "The API returned a transient failure."
        };
    }

    private CancellationTokenSource RegisterLoadJobsCancellationTokenSource() {
        var cancellationTokenSource = new CancellationTokenSource();
        lock (_loadJobsCancellationSync) {
            _loadJobsCancellationTokenSource?.Dispose();
            _loadJobsCancellationTokenSource = cancellationTokenSource;
        }

        return cancellationTokenSource;
    }

    private void ClearLoadJobsCancellationTokenSource(CancellationTokenSource cancellationTokenSource) {
        lock (_loadJobsCancellationSync) {
            if (ReferenceEquals(_loadJobsCancellationTokenSource, cancellationTokenSource)) {
                _loadJobsCancellationTokenSource = null;
            }
        }

        cancellationTokenSource.Dispose();
    }

    private void CancelLoadJobs() {
        lock (_loadJobsCancellationSync) {
            _loadJobsCancellationTokenSource?.Cancel();
        }
    }

    private async Task WaitForLoadJobsIdleAsync() {
        await _loadJobsSemaphore.WaitAsync().ConfigureAwait(false);
        _loadJobsSemaphore.Release();
    }

    private void ApplyLocalProcessorState(LocalProcessorHealthResponse? health) {
        if (health is null) {
            IsLocalProcessorRunning = false;
            IsLocalProcessorAcceptingWork = false;
            LocalProcessorActiveJobs = 0;
            LocalProcessorStatusMessage = _configService.IsLocalProcessorConfigured
                ? "Local processor is not running."
                : "Configure the local processor in Settings to start it from this page.";
        }
        else {
            IsLocalProcessorRunning = true;
            IsLocalProcessorAcceptingWork = health.AcceptingWork;
            LocalProcessorActiveJobs = health.ActiveJobs;
            LocalProcessorStatusMessage = health.Message;
        }

        var output = _localProcessorService.RecentProcessOutput;
        LocalProcessorDiagnosticOutput = output.Count > 0
            ? string.Join(Environment.NewLine, output)
            : string.Empty;
    }

    private static IngestionJobConfiguration? CreateDefaultIngestionConfiguration(string documentType) {
        if (string.Equals(documentType, "bike-graph", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return new IngestionJobConfiguration {
            ExtractGraphRelationships = true,
            OcrEnabled = true
        };
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
            lock (_loadJobsCancellationSync) {
                _loadJobsCancellationTokenSource?.Dispose();
                _loadJobsCancellationTokenSource = null;
            }

            _loadJobsSemaphore.Dispose();
        }
    }

    ~JobsViewModel() {
        Dispose(false);
    }
}
