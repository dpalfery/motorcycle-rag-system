using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Utilities;
using MotorcycleRAG.Contracts.Models.DTOs;
using Timers = System.Timers;

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
    private static readonly string[] CsvExtensions = [".csv"];
    private static readonly string[] CsvMimeTypes = ["csv"];
    private readonly ApiClient _apiClient;
    private readonly IAdminAuthService _authService;
    private readonly IConfigurationStateService _configService;
    private readonly ILogger<JobsViewModel> _logger;
    private readonly System.Timers.Timer _pollTimer;
    private bool _isLoading;
    private bool _isPolling;
    private bool _isStartingLocalGraphJob;
    private string _selectedLocalGraphFilePath = string.Empty;
    private string _localGraphStatusMessage = string.Empty;

    public JobsViewModel(
        ApiClient apiClient,
        IAdminAuthService authService,
        IConfigurationStateService configService,
        ILogger<JobsViewModel> logger) {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        PendingStorageFiles = new ObservableCollection<PendingStorageFileViewModel>();
        IngestionJobs = new ObservableCollection<IngestionJobHistoryViewModel>();
        Jobs = new ObservableCollection<JobViewModel>();

        _pollTimer = new System.Timers.Timer(5000);
        _pollTimer.Elapsed += OnPollTimerElapsed;
        _pollTimer.AutoReset = true;

        LoadJobsCommand = new Command(async () => await LoadJobsAsync().ConfigureAwait(false));
        CancelJobCommand = new Command<string>(async executionId => await CancelJobAsync(executionId).ConfigureAwait(false));
        ProcessPendingStorageFileCommand = new Command<PendingStorageFileViewModel>(
            async pendingFile => await ProcessPendingStorageFileAsync(pendingFile).ConfigureAwait(false));
        ProcessPendingStorageFileAsGraphCommand = new Command<PendingStorageFileViewModel>(
            async pendingFile => await ProcessPendingStorageFileAsync(pendingFile, "bike-graph").ConfigureAwait(false));
        SelectLocalGraphFileCommand = new Command(async () => await SelectLocalGraphFileAsync());
        StartLocalGraphJobCommand = new Command(async () => await StartLocalGraphJobAsync());
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PendingStorageFileViewModel> PendingStorageFiles { get; }

    public ObservableCollection<IngestionJobHistoryViewModel> IngestionJobs { get; }

    public ObservableCollection<JobViewModel> Jobs { get; }

    public bool IsLoading {
        get => _isLoading;
        set {
            if (_isLoading != value) {
                _isLoading = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelectLocalGraphFile)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartLocalGraphJob)));
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

    public bool HasSelectedLocalGraphFile => !string.IsNullOrWhiteSpace(SelectedLocalGraphFilePath);

    public bool HasLocalGraphStatusMessage => !string.IsNullOrWhiteSpace(LocalGraphStatusMessage);

    public bool CanSelectLocalGraphFile => !IsLoading && !IsStartingLocalGraphJob;

    public bool CanStartLocalGraphJob =>
        !IsLoading && !IsStartingLocalGraphJob && !string.IsNullOrWhiteSpace(SelectedLocalGraphFilePath);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand LoadJobsCommand { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand CancelJobCommand { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand ProcessPendingStorageFileCommand { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand ProcessPendingStorageFileAsGraphCommand { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand SelectLocalGraphFileCommand { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public ICommand StartLocalGraphJobCommand { get; }

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

    private async void OnPollTimerElapsed(object? sender, Timers.ElapsedEventArgs e) {
        if (_isPolling) {
            return;
        }

        _isPolling = true;

        await MainThread.InvokeOnMainThreadAsync(async () => {
            try {
                var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
                if (!authorized) {
                    return;
                }

                var runningJobs = new List<JobViewModel>();
                for (int i = 0; i < IngestionJobs.Count; i++) {
                    if (IngestionJobs[i].IsRunning) {
                        await LoadJobsAsync().ConfigureAwait(false);
                        return;
                    }
                }

                for (int i = 0; i < Jobs.Count; i++) {
                    var job = Jobs[i];
                    if (job.IsRunning) {
                        runningJobs.Add(job);
                    }
                }

                foreach (var job in runningJobs) {
                    var statusResponse = await _apiClient.GetPipelineStatusAsync(job.ExecutionId).ConfigureAwait(false);
                    job.Status = statusResponse.Status;

                    if (!job.IsRunning) {
                        await LoadJobsAsync().ConfigureAwait(false);
                        return;
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

            var executionsTask = _apiClient.GetPipelineExecutionsAsync(default);
            var pendingFilesTask = TryGetPendingStorageFilesAsync();
            var ingestionJobsTask = TryGetIngestionJobsAsync();
            await Task.WhenAll(executionsTask, pendingFilesTask, ingestionJobsTask).ConfigureAwait(false);

            var executions = await executionsTask.ConfigureAwait(false);
            var pendingFiles = await pendingFilesTask.ConfigureAwait(false);
            var ingestionJobs = await ingestionJobsTask.ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() => {
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
            }).ConfigureAwait(false);
        }
        catch (HttpRequestException) {
            await MainThread.InvokeOnMainThreadAsync(() => {
                PendingStorageFiles.Clear();
                IngestionJobs.Clear();
                Jobs.Clear();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            await MainThread.InvokeOnMainThreadAsync(() => {
                PendingStorageFiles.Clear();
                IngestionJobs.Clear();
                Jobs.Clear();
            }).ConfigureAwait(false);
        }
        finally {
            IsLoading = false;
        }
    }

    private async Task CancelJobAsync(string executionId) {
        try {
            var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
            if (window?.Page is null) {
                return;
            }

            var confirm = await window.Page.DisplayAlertAsync(
                "Cancel Job",
                $"Are you sure you want to cancel execution {executionId}?",
                "Yes",
                "No").ConfigureAwait(false);

            if (!confirm) {
                return;
            }

            var result = await _apiClient.CancelPipelineAsync(executionId).ConfigureAwait(false);
            if (result.Cancelled) {
                await window.Page.DisplayAlertAsync("Success", "Job cancelled successfully", "OK").ConfigureAwait(false);
                await LoadJobsAsync().ConfigureAwait(false);
            }
            else {
                await window.Page.DisplayAlertAsync("Error", "Failed to cancel job", "OK").ConfigureAwait(false);
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

    private async Task ProcessPendingStorageFileAsync(PendingStorageFileViewModel? pendingFile) {
        await ProcessPendingStorageFileAsync(pendingFile, pendingFile?.DocumentType ?? string.Empty).ConfigureAwait(false);
    }

    private async Task SelectLocalGraphFileAsync() {
        if (IsStartingLocalGraphJob) {
            return;
        }

        try {
            var result = await FilePicker.PickAsync(new PickOptions {
                PickerTitle = "Select a CSV file for graph import",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, CsvExtensions },
                    { DevicePlatform.macOS, CsvMimeTypes }
                })
            });

            if (result is null) {
                return;
            }

            ValidateLocalGraphFile(result.FullPath);
            SelectedLocalGraphFilePath = result.FullPath;
            LocalGraphStatusMessage = $"Selected CSV: {Path.GetFileName(result.FullPath)}";
        }
        catch (Exception ex) {
            await ErrorPresenter.ShowErrorAsync(
                "File Selection Error",
                ErrorPresenter.SanitizeErrorMessage(ex.Message));
            _logger.LogWarning(ex, "Failed to select a local CSV for graph import.");
        }
    }

    private async Task StartLocalGraphJobAsync() {
        if (IsStartingLocalGraphJob || string.IsNullOrWhiteSpace(SelectedLocalGraphFilePath)) {
            return;
        }

        var authorized = await EnsureAuthorizedAsync();
        if (!authorized) {
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
        LocalGraphStatusMessage = $"Uploading {fileName} to raw storage...";

        try {
            var upload = await _apiClient.UploadIngestionSourceAsync(
                selectedPath,
                "bike-graph",
                default);

            LocalGraphStatusMessage = $"Queued graph import for {fileName}...";

            var result = await _apiClient.StartIngestionJobAsync(new IngestionJobStartRequest {
                UploadId = upload.UploadId,
                DocumentType = "bike-graph",
                Configuration = null
            }, default);

            SelectedLocalGraphFilePath = string.Empty;
            LocalGraphStatusMessage = $"Graph import queued with job {result.JobId}.";

            var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
            if (window?.Page != null) {
                await window.Page.DisplayAlertAsync(
                    "Graph Import Queued",
                    $"Uploaded {fileName} and queued graph import as job {result.JobId}.",
                    "OK");
            }

            await LoadJobsAsync();
        }
        catch (UnauthorizedAccessException ex) {
            await ErrorPresenter.ShowWarningAsync(
                "Access Denied",
                "You do not have permission to upload a CSV and start a graph import.");
            _logger.LogWarning(ex, "User not authorized to upload and queue local graph import for {FileName}", fileName);
        }
        catch (HttpRequestException ex) {
            var message = ex.StatusCode == HttpStatusCode.BadRequest
                ? "The API rejected the CSV upload or graph import request. Check that the selected file is a valid CSV and the API is running in the expected mode."
                : "Failed to upload the CSV or queue the graph import job.";

            await ErrorPresenter.ShowErrorAsync("Graph Import Failed", message);
            LocalGraphStatusMessage = "Graph import failed before the job could be queued.";
            _logger.LogWarning(ex, "Failed to upload and queue local graph import for {FileName}", fileName);
        }
        catch (OperationCanceledException ex) {
            LocalGraphStatusMessage = "Graph import request timed out.";
            _logger.LogWarning(ex, "Timed out uploading and queueing local graph import for {FileName}", fileName);
        }
        catch (Exception ex) {
            LocalGraphStatusMessage = "Graph import failed before the job could be queued.";
            await ErrorPresenter.ShowErrorAsync(
                "Graph Import Failed",
                ErrorPresenter.SanitizeErrorMessage(ex.Message));
            _logger.LogError(ex, "Unexpected failure uploading and queueing local graph import for {FileName}", fileName);
        }
        finally {
            IsStartingLocalGraphJob = false;
        }
    }

    private async Task ProcessPendingStorageFileAsync(PendingStorageFileViewModel? pendingFile, string workflowDocumentType) {
        if (pendingFile is null || pendingFile.IsProcessing) {
            return;
        }

        pendingFile.IsProcessing = true;
        try {
            var request = new IngestionJobStartRequest {
                UploadId = pendingFile.UploadId,
                DocumentType = workflowDocumentType,
                Configuration = CreateDefaultIngestionConfiguration(workflowDocumentType)
            };

            var result = await _apiClient.StartIngestionJobAsync(request, default).ConfigureAwait(false);
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
                    "OK").ConfigureAwait(false);
            }

            await LoadJobsAsync().ConfigureAwait(false);
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

            await ErrorPresenter.ShowErrorAsync("Queue Failed", message).ConfigureAwait(false);
            _logger.LogWarning(ex, "Failed to queue pending storage file {UploadId} for workflow {WorkflowDocumentType}", pendingFile.UploadId, workflowDocumentType);
        }
        catch (OperationCanceledException ex) {
            _logger.LogWarning(ex, "Queue operation timed out for upload {UploadId} and workflow {WorkflowDocumentType}", pendingFile.UploadId, workflowDocumentType);
        }
        finally {
            pendingFile.IsProcessing = false;
        }
    }

    private async Task<List<PendingStorageFileDto>> TryGetPendingStorageFilesAsync() {
        try {
            return await _apiClient.GetPendingStorageFilesAsync(default).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
            _logger.LogInformation(ex, "Pending storage files endpoint is not available on the current API.");
            return [];
        }
    }

    private async Task<List<IngestionJobStatusResponse>> TryGetIngestionJobsAsync() {
        try {
            return await _apiClient.GetIngestionJobsAsync(RecentIngestionJobCount, default).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
            _logger.LogInformation(ex, "Ingestion jobs endpoint is not available on the current API.");
            return [];
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
            _pollTimer.Stop();
            _pollTimer.Dispose();
        }
    }

    ~JobsViewModel() {
        Dispose(false);
    }
}
