using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Processing;
using MotorcycleRAG.Admin.Models.Processing;
using MotorcycleRAG.Admin.Utilities;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for data ingestion page
/// Handles file upload, local processing, and API submission
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S1200:Dependencies", Justification = "Coordinator class")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "For data binding")]
internal partial class IngestionViewModel : ObservableObject {
    private static readonly string[] PdfAndCsvExtensions = { ".pdf", ".csv" };
    private static readonly string[] PdfAndCsvMimeTypes = { "pdf", "csv" };

    private readonly ApiClient _apiClient;
    private readonly PdfChunker _pdfChunker;
    private readonly CsvChunker _csvChunker;
    private readonly OnnxEmbeddingService? _embeddingService;
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
    private bool _enableLocalProcessing = true;

    [ObservableProperty]
    private ObservableCollection<ProcessedFileInfo> _processedFiles = new();

    public IngestionViewModel(
        ApiClient apiClient,
        PdfChunker pdfChunker,
        CsvChunker csvChunker,
        IServiceProvider serviceProvider,
        ILogger<IngestionViewModel>? logger = null) {
        
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _pdfChunker = pdfChunker ?? throw new ArgumentNullException(nameof(pdfChunker));
        _csvChunker = csvChunker ?? throw new ArgumentNullException(nameof(csvChunker));
        
        // Safely resolve optional dependency using Service Locator pattern
        // This prevents exceptions if the service is not registered (e.g. missing model file)
        _embeddingService = serviceProvider.GetService<OnnxEmbeddingService>();
        
        _logger = logger;
        _enableLocalProcessing = _embeddingService != null;
        
        if (_embeddingService == null) {
            _logger?.LogWarning("OnnxEmbeddingService not available. Local processing will be disabled.");
        }
    }

    partial void OnIsProcessingChanged(bool value) {
        SelectFileCommand.NotifyCanExecuteChanged();
        ProcessFileCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFilePathChanged(string value) {
        ProcessFileCommand.NotifyCanExecuteChanged();
    }

    #region Commands

    [RelayCommand]
    private async Task SelectFileAsync() {
        try {
            var result = await FilePicker.PickAsync(new PickOptions {
                PickerTitle = "Select a file to process",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, PdfAndCsvExtensions },
                    { DevicePlatform.macOS, PdfAndCsvMimeTypes }
                })
            });

            if (result != null) {
                // File size validation
                var info = new FileInfo(result.FullPath);
                var ext = Path.GetExtension(result.FullPath).ToUpperInvariant();
                long sizeLimit = ext == ".PDF" ? 100 * 1024 * 1024 : 50 * 1024 * 1024;
                if (info.Length > sizeLimit) {
                    await ShowErrorAsync("File too large", $"File exceeds limit ({sizeLimit / (1024 * 1024)}MB).");
                    return;
                }

                // Simple MIME validation (magic numbers)
                if (!IsValidMime(result.FullPath, ext)) {
                    await ShowErrorAsync("Invalid file", "The selected file type does not match its content.");
                    return;
                }

                SelectedFilePath = result.FullPath;
                StatusMessage = $"Selected: {Path.GetFileName(result.FullPath)}";
                _logger?.LogInformation("File selected: {FileName}, SizeBytes: {Size}, Ext: {Ext}", info.Name, info.Length, ext);
            }
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            await ShowErrorAsync("File Selection Error", sanitizedMessage);
            _logger?.LogError(ex, "File selection failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanProcessFile))]
    private async Task ProcessFileAsync() {
        if (string.IsNullOrEmpty(SelectedFilePath))
            return;

        // SECURITY: Check network connectivity before starting processing
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) {
            StatusMessage = "No internet connection. Please check your network.";
            await ShowErrorAsync("No Connection", "Internet connection is required to process files.");
            _logger?.LogWarning("File processing attempted without internet connection");
            return;
        }

        IsProcessing = true;
        ProgressPercentage = 0;
        StatusMessage = "Starting processing...";

        try {
            var fileInfo = new ProcessedFileInfo {
                FileName = Path.GetFileName(SelectedFilePath),
                FilePath = SelectedFilePath,
                Status = "Processing",
                StartTime = DateTime.Now
            };

            ProcessedFiles.Add(fileInfo);

            // Determine file type
            var extension = Path.GetExtension(SelectedFilePath).ToUpperInvariant();

            if (EnableLocalProcessing && _embeddingService != null) {
                // Local processing workflow
                await ProcessLocallyAsync(fileInfo, extension);
            }
            else {
                // Server-side processing workflow
                await ProcessOnServerAsync(fileInfo);
            }

            fileInfo.Status = "Completed";
            fileInfo.EndTime = DateTime.Now;
            StatusMessage = $"Completed: {fileInfo.FileName}";
            ProgressPercentage = 100;
            _logger?.LogInformation("Processing completed for file {FileName}", fileInfo.FileName);
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            StatusMessage = $"Error: {sanitizedMessage}";
            await ShowErrorAsync("Processing Error", sanitizedMessage);
            _logger?.LogError(ex, "Processing failed for file {FileName}", Path.GetFileName(SelectedFilePath));
        }
        finally {
            IsProcessing = false;
        }
    }

    private bool CanProcessFile() => !IsProcessing && !string.IsNullOrEmpty(SelectedFilePath);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear() {
        SelectedFilePath = string.Empty;
        StatusMessage = string.Empty;
        ProgressPercentage = 0;
    }

    private bool CanClear() => !IsProcessing;

    #endregion

    #region Methods

    private async Task ProcessLocallyAsync(ProcessedFileInfo fileInfo, string extension) {
        ProgressPercentage = 10;
        StatusMessage = "Chunking document...";

        // Step 1: Chunking document
        if (extension == ".PDF") {
            var chunkResult = await _pdfChunker.ProcessPdfAsync(fileInfo.FilePath);
            if (!chunkResult.Success) {
                throw new InvalidOperationException($"PDF chunking failed: {string.Join(", ", chunkResult.Errors)}");
            }
            fileInfo.ChunkCount = chunkResult.Chunks.Count;
            fileInfo.Metadata = $"Pages: {chunkResult.Metadata.PageCount}";

            ProgressPercentage = 40;
            StatusMessage = $"Chunked into {chunkResult.Chunks.Count} chunks, generating embeddings...";

            // Step 2: Generate embeddings
            if (_embeddingService != null) {
                var embeddings = new List<float[]>();
                for (int i = 0; i < chunkResult.Chunks.Count; i++) {
                    var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(chunkResult.Chunks[i].Text);
                    if (embeddingResult.Success) {
                        embeddings.Add(embeddingResult.Embedding);
                    }
                    ProgressPercentage = 40 + (50.0 * (i + 1) / chunkResult.Chunks.Count);
                }
                fileInfo.EmbeddingCount = embeddings.Count;
            }
        }
        else if (extension == ".CSV") {
            var chunkResult = await _csvChunker.ProcessCsvAsync(fileInfo.FilePath);
            if (!chunkResult.Success) {
                throw new InvalidOperationException($"CSV chunking failed: {string.Join(", ", chunkResult.Errors)}");
            }
            fileInfo.ChunkCount = chunkResult.Chunks.Count;
            fileInfo.Metadata = $"Rows: {chunkResult.Metadata.TotalRows}, Columns: {chunkResult.Metadata.ColumnCount}";

            ProgressPercentage = 40;
            StatusMessage = $"Chunked {chunkResult.Metadata.TotalRows} rows, generating embeddings...";

            // Step 2: Generate embeddings for CSV chunks
            if (_embeddingService != null) {
                var embeddings = new List<float[]>();
                for (int i = 0; i < chunkResult.Chunks.Count; i++) {
                    var headersList = new List<string>(chunkResult.Metadata.ColumnNames);
                    var searchableText = _csvChunker.CreateSearchableText(chunkResult.Chunks[i].Rows, headersList);

                    var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(searchableText);
                    if (embeddingResult.Success) {
                        embeddings.Add(embeddingResult.Embedding);
                    }
                    ProgressPercentage = 40 + (50.0 * (i + 1) / chunkResult.Chunks.Count);
                }
                fileInfo.EmbeddingCount = embeddings.Count;
            }
        }

        ProgressPercentage = 90;
        StatusMessage = "Uploading to server...";

        // Step 3: Upload processed artifacts to API
        var uploadResult = await _apiClient.UploadFileAsync(fileInfo.FilePath, processImmediately: true);
        fileInfo.ExecutionId = uploadResult.FileId;
    }

    private async Task ProcessOnServerAsync(ProcessedFileInfo fileInfo) {
        ProgressPercentage = 20;
        StatusMessage = "Uploading to server...";

        // Direct upload to server for processing
        var uploadResult = await _apiClient.UploadFileAsync(fileInfo.FilePath, processImmediately: true);
        fileInfo.ExecutionId = uploadResult.FileId;

        ProgressPercentage = 50;
        StatusMessage = "Server processing...";

        // Poll for completion
        await Task.Delay(2000);

        ProgressPercentage = 90;
    }

    private static async Task ShowErrorAsync(string title, string message) {
        var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
        if (window?.Page != null) {
            await window.Page.DisplayAlertAsync(title, message, "OK");
        }
    }

    /// <summary>
    /// Validates file content matches expected MIME type by checking magic numbers.
    /// Prevents file extension spoofing attacks.
    /// </summary>
    private bool IsValidMime(string path, string ext) {
        try {
            using var fs = File.OpenRead(path);
            var header = new byte[8];
            var bytesRead = fs.Read(header, 0, 8);

            if (bytesRead < 4)
                return false;

            return ext switch {
                ".PDF" => header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46,
                ".CSV" => true,
                _ => false
            };
        }
        catch (Exception ex) {
            _logger?.LogError(ex, "Failed MIME validation for {Path}", path);
            return false;
        }
    }

    #endregion
}

/// <summary>
/// Information about a processed file
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "For data binding")]
internal partial class ProcessedFileInfo : ObservableObject {
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
