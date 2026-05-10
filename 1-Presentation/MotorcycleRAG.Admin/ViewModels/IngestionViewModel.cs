using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Models.Processing;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Utilities;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for the manual PDF ingestion page.
/// Handles PDF upload and queueing of the local ingestion job.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S1200:Dependencies", Justification = "Coordinator class")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "For data binding")]
internal partial class IngestionViewModel : ObservableObject
{
    private static readonly string[] PdfFileTypes = { ".pdf" };
    private static readonly string[] PdfMimeTypes = { "pdf" };

    private readonly ApiClient _apiClient;
    private readonly IAdminAuthService _authService;
    private readonly IConfigurationStateService _configService;
    private readonly ILogger<IngestionViewModel>? _logger;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private string _selectedFilePath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ProcessedFileInfo> _processedFiles = new();

    public IngestionViewModel(
        ApiClient apiClient,
        IAdminAuthService authService,
        IConfigurationStateService configService,
        ILogger<IngestionViewModel>? logger = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger;
    }

    partial void OnIsProcessingChanged(bool value)
    {
        SelectFileCommand.NotifyCanExecuteChanged();
        ProcessFileCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFilePathChanged(string value)
    {
        ProcessFileCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task SelectFileAsync()
    {
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Select a PDF to upload",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, PdfFileTypes },
                    { DevicePlatform.macOS, PdfMimeTypes }
                })
            }).ConfigureAwait(false);

            if (result == null)
            {
                return;
            }

            var fileValidation = await MauiThreading.RunOffMainThreadAsync(
                () => ValidateSelectedFile(result.FullPath)).ConfigureAwait(false);

            if (!fileValidation.IsValid)
            {
                await ShowErrorAsync(fileValidation.ErrorTitle!, fileValidation.ErrorMessage!).ConfigureAwait(false);
                return;
            }

            await MauiThreading.RunOnMainThreadAsync(() =>
            {
                SelectedFilePath = result.FullPath;
                StatusMessage = $"Selected: {Path.GetFileName(result.FullPath)}";
            }).ConfigureAwait(false);

            _logger?.LogInformation(
                "File selected: {FileName}, SizeBytes: {Size}, Ext: {Ext}",
                fileValidation.FileName,
                fileValidation.SizeBytes,
                fileValidation.Extension);
        }
        catch (Exception ex)
        {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            await ShowErrorAsync("File Selection Error", sanitizedMessage).ConfigureAwait(false);
            _logger?.LogError(ex, "File selection failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanProcessFile))]
    private async Task ProcessFileAsync()
    {
        if (string.IsNullOrEmpty(SelectedFilePath))
        {
            return;
        }

        var selectedPath = SelectedFilePath;
        var extension = Path.GetExtension(selectedPath).ToUpperInvariant();
        if (!string.Equals(extension, ".PDF", StringComparison.OrdinalIgnoreCase))
        {
            await ShowErrorAsync("Invalid file", "Only PDF files are supported for manual ingestion.").ConfigureAwait(false);
            return;
        }

        var fileInfo = new ProcessedFileInfo
        {
            FileName = Path.GetFileName(selectedPath),
            FilePath = selectedPath,
            Status = "Uploading",
            StartTime = DateTime.UtcNow
        };

        await MauiThreading.RunOnMainThreadAsync(() =>
        {
            IsProcessing = true;
            ProgressPercentage = 0;
            StatusMessage = "Uploading PDF to the backend...";
            ProcessedFiles.Add(fileInfo);
        }).ConfigureAwait(false);

        try
        {
            var uploadResult = await _apiClient.UploadIngestionSourceAsync(selectedPath, "manual-pdf", default).ConfigureAwait(false);
            await MauiThreading.RunOnMainThreadAsync(() =>
            {
                ProgressPercentage = 35;
                StatusMessage = "Queued ingestion job...";
            }).ConfigureAwait(false);

            var request = new IngestionJobStartRequest
            {
                UploadId = uploadResult.UploadId,
                DocumentType = "manual-pdf",
                Configuration = CreateDefaultIngestionConfiguration("manual-pdf")
            };

            var jobResult = await _apiClient.StartIngestionJobAsync(request, default).ConfigureAwait(false);

            await MauiThreading.RunOnMainThreadAsync(() =>
            {
                fileInfo.Status = "Queued";
                fileInfo.ExecutionId = jobResult.JobId.ToString();
                fileInfo.Metadata = $"Upload ID: {uploadResult.UploadId}";
                StatusMessage = $"Queued job {jobResult.JobId} for {fileInfo.FileName}.";
                ProgressPercentage = 100;
            }).ConfigureAwait(false);

            _logger?.LogInformation("Upload queued for file {FileName} with job {JobId}", fileInfo.FileName, jobResult.JobId);
        }
        catch (UnauthorizedAccessException ex)
        {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            await MauiThreading.RunOnMainThreadAsync(() =>
            {
                fileInfo.Status = "Failed";
                StatusMessage = $"Error: {sanitizedMessage}";
            }).ConfigureAwait(false);
            await ShowErrorAsync("Authentication Error", sanitizedMessage).ConfigureAwait(false);
            _logger?.LogWarning(ex, "Unauthorized upload attempt for file {FileName}", fileInfo.FileName);
        }
        catch (Exception ex)
        {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            await MauiThreading.RunOnMainThreadAsync(() =>
            {
                fileInfo.Status = "Failed";
                StatusMessage = $"Error: {sanitizedMessage}";
            }).ConfigureAwait(false);
            await ShowErrorAsync("Upload Error", sanitizedMessage).ConfigureAwait(false);
            _logger?.LogError(ex, "Failed to upload and queue file {FileName}", fileInfo.FileName);
        }
        finally
        {
            await MauiThreading.RunOnMainThreadAsync(() => IsProcessing = false).ConfigureAwait(false);
        }
    }

    private bool CanProcessFile() => !IsProcessing && !string.IsNullOrEmpty(SelectedFilePath);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        SelectedFilePath = string.Empty;
        StatusMessage = string.Empty;
        ProgressPercentage = 0;
    }

    private bool CanClear() => !IsProcessing;

    internal async Task InitializeAsync()
    {
        await MauiThreading.RunOnMainThreadAsync(() => StatusMessage = string.Empty).ConfigureAwait(false);

        if (!_configService.IsApiConfigured)
        {
            await MauiThreading.RunOnMainThreadAsync(() =>
                StatusMessage = "API not configured. Configure API base URL in Settings.").ConfigureAwait(false);
            await ShowWarningAsync(
                "Configuration Required",
                "API is not configured. Go to Settings to configure the API base URL before uploading files.").ConfigureAwait(false);
            return;
        }

        if (!_authService.IsSignedIn())
        {
            await MauiThreading.RunOnMainThreadAsync(() =>
                StatusMessage = "Sign in required to upload files.").ConfigureAwait(false);
            await ShowWarningAsync(
                "Sign In Required",
                "Please sign in before uploading files.").ConfigureAwait(false);
            return;
        }

        await MauiThreading.RunOnMainThreadAsync(() =>
            StatusMessage = "Ready to upload a PDF for local ingestion.").ConfigureAwait(false);
    }

    private Task SetProgressAsync(double percentage, string statusMessage)
    {
        return MauiThreading.RunOnMainThreadAsync(() =>
        {
            ProgressPercentage = percentage;
            StatusMessage = statusMessage;
        });
    }

    private static Task ShowErrorAsync(string title, string message) => ErrorPresenter.ShowErrorAsync(title, message);

    private static Task ShowWarningAsync(string title, string message) => ErrorPresenter.ShowWarningAsync(title, message);

    private static FileValidationResult ValidateSelectedFile(string fullPath)
    {
        var info = new FileInfo(fullPath);
        var extension = Path.GetExtension(fullPath).ToUpperInvariant();
        var sizeLimit = 100L * 1024 * 1024;

        if (!string.Equals(extension, ".PDF", StringComparison.OrdinalIgnoreCase))
        {
            return new FileValidationResult(
                false,
                "Unsupported file type",
                "Only PDF files are supported for manual ingestion.",
                info.Name,
                info.Length,
                extension);
        }

        if (info.Length > sizeLimit)
        {
            return new FileValidationResult(
                false,
                "File too large",
                $"File exceeds limit ({sizeLimit / (1024 * 1024)}MB).",
                info.Name,
                info.Length,
                extension);
        }

        if (!IsValidMime(fullPath, extension))
        {
            return new FileValidationResult(
                false,
                "Invalid file",
                "The selected file type does not match its content.",
                info.Name,
                info.Length,
                extension);
        }

        return new FileValidationResult(true, null, null, info.Name, info.Length, extension);
    }

    private static IngestionJobConfiguration CreateDefaultIngestionConfiguration(string documentType)
    {
        return new IngestionJobConfiguration {
            ExtractGraphRelationships = true,
            OcrEnabled = true
        };
    }

    /// <summary>
    /// Validates file content matches expected MIME type by checking magic numbers.
    /// Prevents file extension spoofing attacks.
    /// </summary>
    private static bool IsValidMime(string path, string extension)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var header = new byte[8];
            var bytesRead = fs.Read(header, 0, 8);

            if (bytesRead < 4)
            {
                return false;
            }

            return extension switch
            {
                ".PDF" => header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46,
                ".CSV" => true,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private sealed record FileValidationResult(
        bool IsValid,
        string? ErrorTitle,
        string? ErrorMessage,
        string FileName,
        long SizeBytes,
        string Extension);
}

/// <summary>
/// Information about a processed file.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "For data binding")]
internal partial class ProcessedFileInfo : ObservableObject
{
    [ObservableProperty]
    private string _status = string.Empty;

    internal string FileName { get; set; } = string.Empty;
    internal string FilePath { get; set; } = string.Empty;
    internal string ExecutionId { get; set; } = string.Empty;
    internal int ChunkCount { get; set; }
    internal int EmbeddingCount { get; set; }
    internal string Metadata { get; set; } = string.Empty;
    internal DateTime StartTime { get; set; }
    internal DateTime? EndTime { get; set; }
}
