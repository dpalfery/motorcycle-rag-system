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

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for the pipeline jobs page.
/// Handles loading pending storage files, ingestion job history, and legacy execution history.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "Internal patterns")]
internal class JobsViewModel : IDisposable, INotifyPropertyChanged {
    private const int RecentIngestionJobCount = 50;
    private const long LocalGraphMaxFileSizeBytes = 2L * 1024 * 1024 * 1024;
    private readonly ApiClient _apiClient;
    private readonly IAdminAuthService _authService;
    private readonly IConfigurationStateService _configService;
    private readonly ILocalProcessorService _localProcessorService;
    private readonly ILogger<JobsViewModel> _logger;
    private readonly SemaphoreSlim _loadJobsSemaphore = new(1, 1);
    private readonly object _loadJobsCancellationSync = new();
    private bool _isLoading;
    private bool _isPickingLocalGraphFile;
    private bool _isStartingLocalGraphJob;
    private bool _isStartingLocalProcessor;
    private bool _isStoppingLocalProcessor;
    private bool _isLocalProcessorRunning;
    private bool _isLocalProcessorAcceptingWork;
    private bool _autoPollIngestionJobsEnabled = true;
    private bool _autoPollPendingStorageFilesEnabled = true;
    private bool _autoPollPipelineExecutionsEnabled = true;
    private int _localProcessorActiveJobs;
    private string _selectedLocalGraphFilePath = string.Empty;
    private string _localGraphStatusMessage = string.Empty;
    private string _localProcessorStatusMessage = "Local processor is not running.";
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

        LoadJobsCommand = new AsyncRelayCommand(() => LoadJobsAsync(forceApiRetry: true));
        CancelJobCommand = new AsyncRelayCommand<string>(CancelJobAsync);
        ProcessPendingStorageFileCommand = new AsyncRelayCommand<PendingStorageFileViewModel>(ProcessPendingStorageFileAsync);
        ProcessPendingStorageFileAsGraphCommand = new AsyncRelayCommand<PendingStorageFileViewModel>(
            pendingFile => ProcessPendingStorageFileAsync(pendingFile, "bike-graph"));
        StartLocalGraphJobCommand = new AsyncRelayCommand(StartLocalGraphJobAsync);
        StartLocalProcessorCommand = new AsyncRelayCommand(StartLocalProcessorAsync);
        StopLocalProcessorCommand = new AsyncRelayCommand(StopLocalProcessorAsync);
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelectLocalGraphFile)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartLocalGraphJob)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartLocalProcessor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStopLocalProcessor)));
            }
        }
    }

    public bool IsStartingLocalGraphJob {
        get => _isStartingLocalGraphJob;
        private set {
            if (_isStartingLocalGraphJob == value) {
                return;
            }

            _isStartingLocalGraphJob = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStartingLocalGraphJob)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelectLocalGraphFile)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartLocalGraphJob)));
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalProcessorStateLabel)));
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalProcessorStateLabel)));
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

    public string SelectedLocalGraphFilePath {
        get => _selectedLocalGraphFilePath;
        private set {
            if (string.Equals(_selectedLocalGraphFilePath, value, StringComparison.Ordinal)) {
                return;
            }

            _selectedLocalGraphFilePath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedLocalGraphFilePath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedLocalGraphFile)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartLocalGraphJob)));
        }
    }

    public string LocalGraphStatusMessage {
        get => _localGraphStatusMessage;
        private set {
            if (string.Equals(_localGraphStatusMessage, value, StringComparison.Ordinal)) {
                return;
            }

            _localGraphStatusMessage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalGraphStatusMessage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLocalGraphStatusMessage)));
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
        }
    }

    public string LocalProcessorStateLabel =>
        !IsLocalProcessorRunning
            ? "Stopped"
            : IsStoppingLocalProcessor || !IsLocalProcessorAcceptingWork
                ? $"Stopping ({LocalProcessorActiveJobs} active jobs)"
                : $"Running ({LocalProcessorActiveJobs} active jobs)";

    public bool HasSelectedLocalGraphFile => !string.IsNullOrWhiteSpace(SelectedLocalGraphFilePath);

    public bool HasLocalGraphStatusMessage => !string.IsNullOrWhiteSpace(LocalGraphStatusMessage);

    public bool CanSelectLocalGraphFile => !IsStartingLocalGraphJob && !_isPickingLocalGraphFile;

    public bool CanStartLocalGraphJob =>
        !IsLoading && !IsStartingLocalGraphJob && !string.IsNullOrWhiteSpace(SelectedLocalGraphFilePath);

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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand LoadJobsCommand { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand CancelJobCommand { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand ProcessPendingStorageFileCommand { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand ProcessPendingStorageFileAsGraphCommand { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand StartLocalGraphJobCommand { get; }

    public ICommand StartLocalProcessorCommand { get; }

    public ICommand StopLocalProcessorCommand { get; }

    public ICommand ImportLocalProcessorJobCommand { get; }

    public async Task InitializeAsync() {
        await LoadJobsAsync(forceApiRetry: true);
    }

    internal Task RefreshAsync() => LoadJobsAsync(forceApiRetry: false);

    private async Task LoadJobsAsync(bool forceApiRetry) {
        if (!MainThread.IsMainThread) {
            await MainThread.InvokeOnMainThreadAsync(() => LoadJobsAsync(forceApiRetry));
            return;
        }

        if (_isPickingLocalGraphFile) {
            return;
        }

        if (!await _loadJobsSemaphore.WaitAsync(0)) {
            return;
        }

        var loadJobsCancellationTokenSource = RegisterLoadJobsCancellationTokenSource();
        var cancellationToken = loadJobsCancellationTokenSource.Token;
        IsLoading = true;
        try {
            cancellationToken.ThrowIfCancellationRequested();
            if (forceApiRetry) {
                ResetAutomaticApiSectionPolling();
            }

            var localHealth = await _localProcessorService.GetHealthAsync(cancellationToken);
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
                await Task.WhenAll(localJobsTask, executionsTask, pendingFilesTask, ingestionJobsTask);
                cancellationToken.ThrowIfCancellationRequested();

                executions = await executionsTask;
                pendingFiles = await pendingFilesTask;
                ingestionJobs = await ingestionJobsTask;
            }
            else {
                await localJobsTask;
                cancellationToken.ThrowIfCancellationRequested();
            }

            var localJobs = await localJobsTask;
            cancellationToken.ThrowIfCancellationRequested();
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
        catch (OperationCanceledException) {
            _logger.LogDebug("Jobs refresh was cancelled.");
        }
        finally {
            IsLoading = false;
            ClearLoadJobsCancellationTokenSource(loadJobsCancellationTokenSource);
            _loadJobsSemaphore.Release();
        }
    }

    private async Task RefreshLocalProcessorSectionAsync(MotorcycleRAG.Admin.Services.Dtos.LocalProcessorHealthResponse? knownHealth = null) {
        if (!MainThread.IsMainThread) {
            await MainThread.InvokeOnMainThreadAsync(() => RefreshLocalProcessorSectionAsync(knownHealth));
            return;
        }

        await _loadJobsSemaphore.WaitAsync();
        try {
            var health = knownHealth ?? await _localProcessorService.GetHealthAsync(default);
            ApplyLocalProcessorState(health);

            var localJobs = health is null
                ? []
                : await TryGetLocalProcessorJobsAsync(default);

            LocalProcessorJobs.Clear();
            foreach (var localJob in localJobs) {
                LocalProcessorJobs.Add(localJob);
            }
        }
        finally {
            _loadJobsSemaphore.Release();
        }
    }

    private async Task CancelJobAsync(string? executionId) {
        if (string.IsNullOrWhiteSpace(executionId)) {
            return;
        }

        try {
            var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
            if (window?.Page is null) {
                return;
            }

            var confirm = await window.Page.DisplayAlertAsync(
                "Cancel Job",
                $"Are you sure you want to cancel execution {executionId}?",
                "Yes",
                "No");

            if (!confirm) {
                return;
            }

            var result = await _apiClient.CancelPipelineAsync(executionId);
            if (result.Cancelled) {
                await window.Page.DisplayAlertAsync("Success", "Job cancelled successfully", "OK");
                await LoadJobsAsync(forceApiRetry: true);
            }
            else {
                await window.Page.DisplayAlertAsync("Error", "Failed to cancel job", "OK");
            }
        }
        catch (UnauthorizedAccessException ex) {
            _logger.LogWarning(ex, "User not authorized to cancel job {ExecutionId}", executionId);
        }
        catch (HttpRequestException ex) {
            var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
            if (window?.Page != null) {
                await window.Page.DisplayAlertAsync("Error", "Failed to reach the API server", "OK");
            }

            _logger.LogWarning(ex, "Failed to reach API server when canceling execution {ExecutionId}", executionId);
        }
        catch (OperationCanceledException ex) {
            _logger.LogWarning(ex, "Cancel job operation timed out for execution {ExecutionId}", executionId);
        }
    }

    private async Task ProcessPendingStorageFileAsync(PendingStorageFileViewModel? pendingFile) {
        await ProcessPendingStorageFileAsync(pendingFile, pendingFile?.DocumentType ?? string.Empty);
    }

    internal async Task<bool> BeginLocalGraphFileSelectionAsync() {
        if (IsStartingLocalGraphJob || _isPickingLocalGraphFile) {
            return false;
        }

        _isPickingLocalGraphFile = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelectLocalGraphFile)));
        CancelLoadJobs();
        await WaitForLoadJobsIdleAsync();
        return true;
    }

    internal void ApplySelectedLocalGraphFile(string filePath) {
        ValidateLocalGraphFile(filePath);
        SelectedLocalGraphFilePath = filePath;
        LocalGraphStatusMessage = $"Selected CSV: {Path.GetFileName(filePath)}";
    }

    internal void EndLocalGraphFileSelection() {
        if (!_isPickingLocalGraphFile) {
            return;
        }

        _isPickingLocalGraphFile = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelectLocalGraphFile)));
    }

    private async Task StartLocalProcessorAsync() {
        IsStartingLocalProcessor = true;
        LocalProcessorStatusMessage = "Starting local processor...";

        try {
            var health = await _localProcessorService.StartAsync();
            await RefreshLocalProcessorSectionAsync(health);
        }
        catch (Exception ex) {
            LocalProcessorStatusMessage = "Failed to start the local processor.";
            await ErrorPresenter.ShowErrorAsync(
                "Local Processor Failed",
                ErrorPresenter.SanitizeErrorMessage(ex.Message));
            _logger.LogError(ex, "Failed to start the local processor.");
        }
        finally {
            IsStartingLocalProcessor = false;
        }
    }

    private async Task StopLocalProcessorAsync() {
        var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
        if (window?.Page is null) {
            return;
        }

        var confirm = await window.Page.DisplayAlertAsync(
            "Stop Local Processor",
            "Stop the local processor gracefully after active jobs finish?",
            "Stop",
            "Cancel");

        if (!confirm) {
            return;
        }

        IsStoppingLocalProcessor = true;
        LocalProcessorStatusMessage = "Stopping local processor gracefully...";

        try {
            await _localProcessorService.StopAsync();
            await RefreshLocalProcessorSectionAsync();
        }
        catch (Exception ex) {
            LocalProcessorStatusMessage = "The local processor did not stop cleanly.";
            await ErrorPresenter.ShowErrorAsync(
                "Stop Failed",
                ErrorPresenter.SanitizeErrorMessage(ex.Message));
            _logger.LogError(ex, "Failed to stop the local processor.");
        }
        finally {
            IsStoppingLocalProcessor = false;
        }
    }

    private async Task StartLocalGraphJobAsync() {
        if (IsStartingLocalGraphJob || string.IsNullOrWhiteSpace(SelectedLocalGraphFilePath)) {
            return;
        }

        try {
            ValidateLocalGraphFile(SelectedLocalGraphFilePath);
        }
        catch (Exception ex) {
            await ErrorPresenter.ShowErrorAsync(
                "Invalid CSV",
                ErrorPresenter.SanitizeErrorMessage(ex.Message));
            return;
        }

        IsStartingLocalGraphJob = true;
        var selectedPath = SelectedLocalGraphFilePath;
        var fileName = Path.GetFileName(selectedPath);

        try {
            var health = await _localProcessorService.GetHealthAsync();
            if (health is null) {
                LocalGraphStatusMessage = "Starting local processor...";
                await _localProcessorService.StartAsync();
            }

            LocalGraphStatusMessage = $"Starting local graph job for {fileName}...";
            var result = await _localProcessorService.StartBikeGraphJobAsync(selectedPath);

            SelectedLocalGraphFilePath = string.Empty;
            LocalGraphStatusMessage = $"Local graph job {result.JobId} started for {fileName}.";
            await RefreshLocalProcessorSectionAsync();
        }
        catch (Exception ex) {
            LocalGraphStatusMessage = "Failed to start the local graph job.";
            await ErrorPresenter.ShowErrorAsync(
                "Local Graph Job Failed",
                ErrorPresenter.SanitizeErrorMessage(ex.Message));
            _logger.LogError(ex, "Failed to start a local graph job for {FileName}", fileName);
        }
        finally {
            IsStartingLocalGraphJob = false;
        }
    }

    private async Task ImportLocalProcessorJobAsync(LocalProcessorJobViewModel? job) {
        if (job is null || job.IsImporting) {
            return;
        }

        var authorized = await EnsureAuthorizedAsync(showErrors: true);
        if (!authorized) {
            return;
        }

        job.IsImporting = true;
        try {
            var result = await _apiClient.ImportGraphArtifactsAsync(
                new GraphImportStartRequest { UploadId = job.UploadId },
                default);

            job.GraphImportStatus = result.Status;
            job.GraphImportFailureReason = result.FailureReason;
            LocalGraphStatusMessage = $"Imported processed graph artifact for upload {job.UploadId}.";
            await LoadJobsAsync(forceApiRetry: true);
        }
        catch (Exception ex) {
            await ErrorPresenter.ShowErrorAsync(
                "Import Failed",
                ErrorPresenter.SanitizeErrorMessage(ex.Message));
            _logger.LogError(ex, "Failed to import processed graph artifact for upload {UploadId}", job.UploadId);
        }
        finally {
            job.IsImporting = false;
        }
    }

    private async Task ProcessPendingStorageFileAsync(PendingStorageFileViewModel? pendingFile, string workflowDocumentType) {
        if (pendingFile is null || pendingFile.IsProcessing) {
            return;
        }

        var authorized = await EnsureAuthorizedAsync(showErrors: true);
        if (!authorized) {
            return;
        }

        pendingFile.IsProcessing = true;
        try {
            var request = new IngestionJobStartRequest {
                UploadId = pendingFile.UploadId,
                DocumentType = workflowDocumentType,
                Configuration = CreateDefaultIngestionConfiguration(workflowDocumentType)
            };

            var result = await _apiClient.StartIngestionJobAsync(request, default);
            var isGraphImport = string.Equals(workflowDocumentType, "bike-graph", StringComparison.OrdinalIgnoreCase);
            var title = isGraphImport ? "Graph Import Queued" : "Ingestion Queued";
            var message = isGraphImport
                ? $"Queued graph import for {pendingFile.BlobName} with job {result.JobId}."
                : $"Queued {pendingFile.BlobName} with job {result.JobId}.";

            var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
            if (window?.Page != null) {
                await window.Page.DisplayAlertAsync(
                    title,
                    message,
                    "OK");
            }

            await LoadJobsAsync(forceApiRetry: true);
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
            pendingFile.IsProcessing = false;
        }
    }

    private async Task<List<PendingStorageFileDto>> TryGetPendingStorageFilesAsync(CancellationToken cancellationToken) {
        try {
            return await _apiClient.GetPendingStorageFilesAsync(cancellationToken);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
            PauseAutomaticApiSectionPolling(
                ref _autoPollPendingStorageFilesEnabled,
                "Pending storage files",
                ex.StatusCode);
            _logger.LogInformation(ex, "Pending storage files endpoint is not available on the current API.");
            return [];
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest) {
            PauseAutomaticApiSectionPolling(
                ref _autoPollPendingStorageFilesEnabled,
                "Pending storage files",
                ex.StatusCode);
            _logger.LogInformation("Pending storage files request was rejected by the API.");
            return [];
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Pending storage files could not be loaded.");
            return [];
        }
    }

    private async Task<List<IngestionJobStatusResponse>> TryGetIngestionJobsAsync(CancellationToken cancellationToken) {
        try {
            return await _apiClient.GetIngestionJobsAsync(RecentIngestionJobCount, cancellationToken);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
            PauseAutomaticApiSectionPolling(
                ref _autoPollIngestionJobsEnabled,
                "Ingestion jobs",
                ex.StatusCode);
            _logger.LogInformation(ex, "Ingestion jobs endpoint is not available on the current API.");
            return [];
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest) {
            PauseAutomaticApiSectionPolling(
                ref _autoPollIngestionJobsEnabled,
                "Ingestion jobs",
                ex.StatusCode);
            _logger.LogInformation("Ingestion jobs request was rejected by the API.");
            return [];
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Ingestion jobs could not be loaded.");
            return [];
        }
    }

    private async Task<List<PipelineExecution>> TryGetPipelineExecutionsAsync(CancellationToken cancellationToken) {
        try {
            return await _apiClient.GetPipelineExecutionsAsync(cancellationToken);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.BadRequest) {
            PauseAutomaticApiSectionPolling(
                ref _autoPollPipelineExecutionsEnabled,
                "Pipeline executions",
                ex.StatusCode);
            _logger.LogInformation("Pipeline executions request was rejected by the API.");
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

        var isAuthorized = await _authService.IsAuthorizedAdminAsync();
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

    private void ResetAutomaticApiSectionPolling() {
        _autoPollPendingStorageFilesEnabled = true;
        _autoPollIngestionJobsEnabled = true;
        _autoPollPipelineExecutionsEnabled = true;
    }

    private void PauseAutomaticApiSectionPolling(
        ref bool isEnabled,
        string sectionName,
        HttpStatusCode? statusCode) {
        if (!isEnabled) {
            return;
        }

        isEnabled = false;
        _logger.LogWarning(
            "{SectionName} automatic polling paused after the API returned {StatusCode}. Use Refresh to retry.",
            sectionName,
            statusCode);
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
        await _loadJobsSemaphore.WaitAsync();
        _loadJobsSemaphore.Release();
    }

    private void ApplyLocalProcessorState(MotorcycleRAG.Admin.Services.Dtos.LocalProcessorHealthResponse? health) {
        if (health is null) {
            IsLocalProcessorRunning = false;
            IsLocalProcessorAcceptingWork = false;
            LocalProcessorActiveJobs = 0;
            LocalProcessorStatusMessage = _configService.IsLocalProcessorConfigured
                ? "Local processor is not running."
                : "Configure the local processor in Settings to start it from this page.";
            return;
        }

        IsLocalProcessorRunning = true;
        IsLocalProcessorAcceptingWork = health.AcceptingWork;
        LocalProcessorActiveJobs = health.ActiveJobs;
        LocalProcessorStatusMessage = health.Message;
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

    private static void ValidateLocalGraphFile(string filePath) {
        if (string.IsNullOrWhiteSpace(filePath)) {
            throw new InvalidOperationException("Choose a CSV file before starting a graph import.");
        }

        if (!File.Exists(filePath)) {
            throw new FileNotFoundException("The selected CSV file no longer exists.", filePath);
        }

        if (!string.Equals(Path.GetExtension(filePath), ".csv", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Only CSV files can be used for graph import.");
        }

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0) {
            throw new InvalidOperationException("The selected CSV file is empty.");
        }

        if (fileInfo.Length > LocalGraphMaxFileSizeBytes) {
            throw new InvalidOperationException("The selected CSV exceeds the 2 GB graph import limit.");
        }
    }

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
