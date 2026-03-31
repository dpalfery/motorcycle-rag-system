using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Models.Processing;
using MotorcycleRAG.Admin.Processing;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Utilities;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for data ingestion page.
/// Handles file upload, local processing, and API submission.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S1200:Dependencies", Justification = "Coordinator class")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "For data binding")]
internal partial class IngestionViewModel : ObservableObject
{
    private static readonly string[] PdfAndCsvExtensions = { ".pdf", ".csv" };
    private static readonly string[] PdfAndCsvMimeTypes = { "pdf", "csv" };

    private readonly ApiClient _apiClient;
    private readonly IAdminAuthService _authService;
    private readonly IConfigurationStateService _configService;
    private readonly PdfChunker _pdfChunker;
    private readonly CsvChunker _csvChunker;
    private readonly SemaphoreSlim _embeddingServiceSemaphore = new(1, 1);
    private readonly ILogger<IngestionViewModel>? _logger;
    private OnnxEmbeddingService? _embeddingService;
    private bool _embeddingServiceChecked;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private string _selectedFilePath = string.Empty;

    [ObservableProperty]
    private bool _enableLocalProcessing = true;

    [ObservableProperty]
    private ObservableCollection<ProcessedFileInfo> _processedFiles = new();

    public IngestionViewModel(
        ApiClient apiClient,
        PdfChunker pdfChunker,
        CsvChunker csvChunker,
        IAdminAuthService authService,
        IConfigurationStateService configService,
        IServiceProvider serviceProvider,
        ILogger<IngestionViewModel>? logger = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _pdfChunker = pdfChunker ?? throw new ArgumentNullException(nameof(pdfChunker));
        _csvChunker = csvChunker ?? throw new ArgumentNullException(nameof(csvChunker));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger;
        _ = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _enableLocalProcessing = false;
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
                PickerTitle = "Select a file to process",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, PdfAndCsvExtensions },
                    { DevicePlatform.macOS, PdfAndCsvMimeTypes }
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

        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            await MauiThreading.RunOnMainThreadAsync(() =>
                StatusMessage = "No internet connection. Please check your network.").ConfigureAwait(false);
            await ShowErrorAsync("No Connection", "Internet connection is required to process files.").ConfigureAwait(false);
            _logger?.LogWarning("File processing attempted without internet connection");
            return;
        }

        var selectedPath = SelectedFilePath;
        var extension = Path.GetExtension(selectedPath).ToUpperInvariant();
        var embeddingService = EnableLocalProcessing
            ? await MauiThreading.RunOffMainThreadAsync(() => EnsureEmbeddingServiceAsync()).ConfigureAwait(false)
            : null;
        var fileInfo = new ProcessedFileInfo
        {
            FileName = Path.GetFileName(selectedPath),
            FilePath = selectedPath,
            Status = "Processing",
            StartTime = DateTime.Now
        };

        await MauiThreading.RunOnMainThreadAsync(() =>
        {
            IsProcessing = true;
            ProgressPercentage = 0;
            StatusMessage = "Starting processing...";
            ProcessedFiles.Add(fileInfo);
        }).ConfigureAwait(false);

        try
        {
            await MauiThreading.RunOffMainThreadAsync(async () =>
            {
                if (EnableLocalProcessing && embeddingService != null)
                {
                    await ProcessLocallyAsync(fileInfo, extension, embeddingService).ConfigureAwait(false);
                }
                else
                {
                    await ProcessOnServerAsync(fileInfo).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await MauiThreading.RunOnMainThreadAsync(() =>
            {
                fileInfo.Status = "Completed";
                fileInfo.EndTime = DateTime.Now;
                StatusMessage = $"Completed: {fileInfo.FileName}";
                ProgressPercentage = 100;
            }).ConfigureAwait(false);

            _logger?.LogInformation("Processing completed for file {FileName}", fileInfo.FileName);
        }
        catch (Exception ex)
        {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);

            await MauiThreading.RunOnMainThreadAsync(() =>
            {
                fileInfo.Status = "Failed";
                fileInfo.EndTime = DateTime.Now;
                StatusMessage = $"Error: {sanitizedMessage}";
            }).ConfigureAwait(false);

            await ShowErrorAsync("Processing Error", sanitizedMessage).ConfigureAwait(false);
            _logger?.LogError(ex, "Processing failed for file {FileName}", Path.GetFileName(selectedPath));
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

        var embeddingService = await MauiThreading.RunOffMainThreadAsync(() => EnsureEmbeddingServiceAsync()).ConfigureAwait(false);
        await MauiThreading.RunOnMainThreadAsync(() => EnableLocalProcessing = embeddingService != null).ConfigureAwait(false);

        if (!EnableLocalProcessing)
        {
            await MauiThreading.RunOnMainThreadAsync(() =>
                StatusMessage = "Local processing unavailable. Files will be processed server-side.").ConfigureAwait(false);
            _logger?.LogInformation("Upload page initialized. Local processing: {Enabled}", EnableLocalProcessing);
        }
        else
        {
            await MauiThreading.RunOnMainThreadAsync(() =>
                StatusMessage = "Ready to process files locally.").ConfigureAwait(false);
            _logger?.LogInformation("Upload page initialized. Local processing enabled with ONNX embeddings.");
        }
    }

    private async Task ProcessLocallyAsync(ProcessedFileInfo fileInfo, string extension, OnnxEmbeddingService embeddingService)
    {
        await SetProgressAsync(10, "Chunking document...").ConfigureAwait(false);

        if (extension == ".PDF")
        {
            var chunkResult = await _pdfChunker.ProcessPdfAsync(fileInfo.FilePath).ConfigureAwait(false);
            if (!chunkResult.Success)
            {
                throw new InvalidOperationException($"PDF chunking failed: {string.Join(", ", chunkResult.Errors)}");
            }

            await MauiThreading.RunOnMainThreadAsync(() =>
            {
                fileInfo.ChunkCount = chunkResult.Chunks.Count;
                fileInfo.Metadata = $"Pages: {chunkResult.Metadata.PageCount}";
            }).ConfigureAwait(false);

            var embeddingStatusMessage = $"Chunked into {chunkResult.Chunks.Count} chunks, generating embeddings...";
            await SetProgressAsync(40, embeddingStatusMessage).ConfigureAwait(false);

            var embeddings = new List<float[]>();
            for (var index = 0; index < chunkResult.Chunks.Count; index++)
            {
                var embeddingResult = await embeddingService.GenerateEmbeddingAsync(chunkResult.Chunks[index].Text).ConfigureAwait(false);
                if (embeddingResult.Success)
                {
                    embeddings.Add(embeddingResult.Embedding);
                }

                await SetProgressAsync(40 + (50.0 * (index + 1) / chunkResult.Chunks.Count), embeddingStatusMessage).ConfigureAwait(false);
            }

            await MauiThreading.RunOnMainThreadAsync(() => fileInfo.EmbeddingCount = embeddings.Count).ConfigureAwait(false);
        }
        else if (extension == ".CSV")
        {
            var chunkResult = await _csvChunker.ProcessCsvAsync(fileInfo.FilePath).ConfigureAwait(false);
            if (!chunkResult.Success)
            {
                throw new InvalidOperationException($"CSV chunking failed: {string.Join(", ", chunkResult.Errors)}");
            }

            await MauiThreading.RunOnMainThreadAsync(() =>
            {
                fileInfo.ChunkCount = chunkResult.Chunks.Count;
                fileInfo.Metadata = $"Rows: {chunkResult.Metadata.TotalRows}, Columns: {chunkResult.Metadata.ColumnCount}";
            }).ConfigureAwait(false);

            var embeddingStatusMessage = $"Chunked {chunkResult.Metadata.TotalRows} rows, generating embeddings...";
            await SetProgressAsync(40, embeddingStatusMessage).ConfigureAwait(false);

            var embeddings = new List<float[]>();
            for (var index = 0; index < chunkResult.Chunks.Count; index++)
            {
                var headersList = new List<string>(chunkResult.Metadata.ColumnNames);
                var searchableText = _csvChunker.CreateSearchableText(chunkResult.Chunks[index].Rows, headersList);
                var embeddingResult = await embeddingService.GenerateEmbeddingAsync(searchableText).ConfigureAwait(false);

                if (embeddingResult.Success)
                {
                    embeddings.Add(embeddingResult.Embedding);
                }

                await SetProgressAsync(40 + (50.0 * (index + 1) / chunkResult.Chunks.Count), embeddingStatusMessage).ConfigureAwait(false);
            }

            await MauiThreading.RunOnMainThreadAsync(() => fileInfo.EmbeddingCount = embeddings.Count).ConfigureAwait(false);
        }

        await SetProgressAsync(90, "Uploading to server...").ConfigureAwait(false);

        var uploadResult = await _apiClient.UploadFileAsync(fileInfo.FilePath, processImmediately: true).ConfigureAwait(false);
        await MauiThreading.RunOnMainThreadAsync(() => fileInfo.ExecutionId = uploadResult.FileId).ConfigureAwait(false);
    }

    private async Task ProcessOnServerAsync(ProcessedFileInfo fileInfo)
    {
        await SetProgressAsync(20, "Uploading to server...").ConfigureAwait(false);

        var uploadResult = await _apiClient.UploadFileAsync(fileInfo.FilePath, processImmediately: true).ConfigureAwait(false);
        await MauiThreading.RunOnMainThreadAsync(() => fileInfo.ExecutionId = uploadResult.FileId).ConfigureAwait(false);

        await SetProgressAsync(50, "Server processing...").ConfigureAwait(false);
        await Task.Delay(2000).ConfigureAwait(false);
        await SetProgressAsync(90, "Server processing...").ConfigureAwait(false);
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

    private async Task<OnnxEmbeddingService?> EnsureEmbeddingServiceAsync()
    {
        if (_embeddingServiceChecked)
        {
            return _embeddingService;
        }

        await _embeddingServiceSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_embeddingServiceChecked)
            {
                return _embeddingService;
            }

            try
            {
                _embeddingService = OnnxEmbeddingServiceFactory.CreateFromAppResources();
            }
            catch (FileNotFoundException)
            {
                _logger?.LogInformation("ONNX embedding model not found. Local processing will stay disabled.");
                _embeddingService = null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to initialize ONNX embedding service. Local processing will stay disabled.");
                _embeddingService = null;
            }

            _embeddingServiceChecked = true;
            return _embeddingService;
        }
        finally
        {
            _embeddingServiceSemaphore.Release();
        }
    }

    private static FileValidationResult ValidateSelectedFile(string fullPath)
    {
        var info = new FileInfo(fullPath);
        var extension = Path.GetExtension(fullPath).ToUpperInvariant();
        var sizeLimit = extension == ".PDF" ? 100L * 1024 * 1024 : 50L * 1024 * 1024;

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
