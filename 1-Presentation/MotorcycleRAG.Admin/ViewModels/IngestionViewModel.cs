using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Processing;
using MotorcycleRAG.Admin.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for the data ingestion workflow
/// Handles file upload, local processing, and API submission
/// </summary>
public class IngestionViewModel : INotifyPropertyChanged
{
    private readonly ApiClient _apiClient;
    private readonly PdfChunker _pdfChunker;
    private readonly CsvChunker _csvChunker;
    private readonly OnnxEmbeddingService? _embeddingService;
    private readonly ILogger<IngestionViewModel>? _logger;
    
    private string _statusMessage = string.Empty;
    private bool _isProcessing;
    private double _progressPercentage;
    private string _selectedFilePath = string.Empty;
    private bool _enableLocalProcessing = true;

    public IngestionViewModel(
        ApiClient apiClient,
        PdfChunker pdfChunker,
        CsvChunker csvChunker,
        OnnxEmbeddingService? embeddingService = null,
        ILogger<IngestionViewModel>? logger = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _pdfChunker = pdfChunker ?? throw new ArgumentNullException(nameof(pdfChunker));
        _csvChunker = csvChunker ?? throw new ArgumentNullException(nameof(csvChunker));
        _embeddingService = embeddingService; // Optional - null means server-side processing only
        _logger = logger;
        
        // Check if local processing is available (requires ONNX model)
        _enableLocalProcessing = _embeddingService != null;

        ProcessedFiles = new ObservableCollection<ProcessedFileInfo>();
        
        // Commands
        SelectFileCommand = new Command(async () => await SelectFileAsync());
        ProcessFileCommand = new Command(async () => await ProcessFileAsync(), () => !IsProcessing && !string.IsNullOrEmpty(SelectedFilePath));
        ClearCommand = new Command(Clear, () => !IsProcessing);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    #region Properties

    public ObservableCollection<ProcessedFileInfo> ProcessedFiles { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            if (SetProperty(ref _isProcessing, value))
            {
                ((Command)ProcessFileCommand).ChangeCanExecute();
                ((Command)ClearCommand).ChangeCanExecute();
            }
        }
    }

    public double ProgressPercentage
    {
        get => _progressPercentage;
        set => SetProperty(ref _progressPercentage, value);
    }

    public string SelectedFilePath
    {
        get => _selectedFilePath;
        set
        {
            if (SetProperty(ref _selectedFilePath, value))
            {
                ((Command)ProcessFileCommand).ChangeCanExecute();
            }
        }
    }

    public bool EnableLocalProcessing
    {
        get => _enableLocalProcessing;
        set => SetProperty(ref _enableLocalProcessing, value);
    }

    #endregion

    #region Commands

    public ICommand SelectFileCommand { get; }
    public ICommand ProcessFileCommand { get; }
    public ICommand ClearCommand { get; }

    #endregion

    #region Methods

    private async Task SelectFileAsync()
    {
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Select a file to process",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".pdf", ".csv" } },
                    { DevicePlatform.macOS, new[] { "pdf", "csv" } }
                })
            });

            if (result != null)
            {
                // File size validation
                var info = new FileInfo(result.FullPath);
                var ext = Path.GetExtension(result.FullPath).ToLowerInvariant();
                long sizeLimit = ext == ".pdf" ? 100 * 1024 * 1024 : 50 * 1024 * 1024; // 100MB PDF, 50MB CSV
                if (info.Length > sizeLimit)
                {
                    await ShowErrorAsync("File too large", $"File exceeds limit ({sizeLimit / (1024 * 1024)}MB).");
                    return;
                }

                // Simple MIME validation (magic numbers)
                if (!IsValidMime(result.FullPath, ext))
                {
                    await ShowErrorAsync("Invalid file", "The selected file type does not match its content.");
                    return;
                }

                SelectedFilePath = result.FullPath;
                StatusMessage = $"Selected: {Path.GetFileName(result.FullPath)}";
                _logger?.LogInformation("File selected: {FileName}, SizeBytes: {Size}, Ext: {Ext}", info.Name, info.Length, ext);
            }
        }
        catch (Exception ex)
        {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            await ShowErrorAsync("File Selection Error", sanitizedMessage);
            _logger?.LogError(ex, "File selection failed");
        }
    }

    private async Task ProcessFileAsync()
    {
        if (string.IsNullOrEmpty(SelectedFilePath))
            return;

        // Check network connectivity before starting processing
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                StatusMessage = "No internet connection. Please check your network.";
                await ShowErrorAsync("No Connection", "Internet connection is required to process files.");
                _logger?.LogWarning("File processing attempted without internet connection");
                return;
            }
        });

        IsProcessing = true;
        ProgressPercentage = 0;
        StatusMessage = "Starting processing...";

        try
        {
            var fileInfo = new ProcessedFileInfo
            {
                FileName = Path.GetFileName(SelectedFilePath),
                FilePath = SelectedFilePath,
                Status = "Processing",
                StartTime = DateTime.Now
            };

            ProcessedFiles.Add(fileInfo);

            // Determine file type
            var extension = Path.GetExtension(SelectedFilePath).ToLowerInvariant();

            if (EnableLocalProcessing && _embeddingService != null)
            {
                // Local processing workflow
                await ProcessLocallyAsync(fileInfo, extension);
            }
            else
            {
                // Server-side processing workflow
                await ProcessOnServerAsync(fileInfo);
            }

            fileInfo.Status = "Completed";
            fileInfo.EndTime = DateTime.Now;
            StatusMessage = $"Completed: {fileInfo.FileName}";
            ProgressPercentage = 100;
            _logger?.LogInformation("Processing completed for file {FileName}", fileInfo.FileName);
        }
        catch (Exception ex)
        {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            StatusMessage = $"Error: {sanitizedMessage}";
            await ShowErrorAsync("Processing Error", sanitizedMessage);
            _logger?.LogError(ex, "Processing failed for file {FilePath}", SelectedFilePath);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task ProcessLocallyAsync(ProcessedFileInfo fileInfo, string extension)
    {
        ProgressPercentage = 10;
        StatusMessage = "Chunking document...";

        // Step 1: Chunk the document
        if (extension == ".pdf")
        {
            var chunkResult = await _pdfChunker.ProcessPdfAsync(fileInfo.FilePath);
            if (!chunkResult.Success)
            {
                throw new Exception($"PDF chunking failed: {string.Join(", ", chunkResult.Errors)}");
            }
            fileInfo.ChunkCount = chunkResult.Chunks.Count;
            fileInfo.Metadata = $"Pages: {chunkResult.Metadata.PageCount}";
            
            ProgressPercentage = 40;
            StatusMessage = $"Chunked into {chunkResult.Chunks.Count} chunks, generating embeddings...";

            // Step 2: Generate embeddings
            if (_embeddingService != null)
            {
                var embeddings = new List<float[]>();
                for (int i = 0; i < chunkResult.Chunks.Count; i++)
                {
                    var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(chunkResult.Chunks[i].Text);
                    if (embeddingResult.Success)
                    {
                        embeddings.Add(embeddingResult.Embedding);
                    }
                    ProgressPercentage = 40 + (50.0 * (i + 1) / chunkResult.Chunks.Count);
                }
                fileInfo.EmbeddingCount = embeddings.Count;
            }
        }
        else if (extension == ".csv")
        {
            var chunkResult = await _csvChunker.ProcessCsvAsync(fileInfo.FilePath);
            if (!chunkResult.Success)
            {
                throw new Exception($"CSV chunking failed: {string.Join(", ", chunkResult.Errors)}");
            }
            fileInfo.ChunkCount = chunkResult.Chunks.Count;
            fileInfo.Metadata = $"Rows: {chunkResult.Metadata.TotalRows}, Columns: {chunkResult.Metadata.ColumnCount}";
            
            ProgressPercentage = 40;
            StatusMessage = $"Chunked {chunkResult.Metadata.TotalRows} rows, generating embeddings...";

            // Step 2: Generate embeddings for CSV chunks
            if (_embeddingService != null)
            {
                var embeddings = new List<float[]>();
                for (int i = 0; i < chunkResult.Chunks.Count; i++)
                {
                    var searchableText = _csvChunker.CreateSearchableText(
                        chunkResult.Chunks[i].Rows, 
                        chunkResult.Metadata.ColumnNames);
                    
                    var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(searchableText);
                    if (embeddingResult.Success)
                    {
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

    private async Task ProcessOnServerAsync(ProcessedFileInfo fileInfo)
    {
        ProgressPercentage = 20;
        StatusMessage = "Uploading to server...";

        // Direct upload to server for processing
        var uploadResult = await _apiClient.UploadFileAsync(fileInfo.FilePath, processImmediately: true);
        fileInfo.ExecutionId = uploadResult.FileId;

        ProgressPercentage = 50;
        StatusMessage = "Server processing...";

        // Poll for completion
        await Task.Delay(2000); // Give server time to start processing

        ProgressPercentage = 90;
    }

    private void Clear()
    {
        SelectedFilePath = string.Empty;
        StatusMessage = string.Empty;
        ProgressPercentage = 0;
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var window = Application.Current?.Windows?.FirstOrDefault();
        if (window?.Page != null)
        {
            await window.Page.DisplayAlertAsync(title, message, "OK");
        }
    }

    private bool IsValidMime(string path, string ext)
    {
        try
        {
            using var fs = File.OpenRead(path);

            // PDF header: 25 50 44 46 => "%PDF"
            if (ext == ".pdf")
            {
                Span<byte> pdfHeader = stackalloc byte[4];
                if (fs.Length < 4) return false;
                int bytesRead = fs.Read(pdfHeader);
                if (bytesRead < 4) return false;
                return pdfHeader[0] == 0x25 && pdfHeader[1] == 0x50 && pdfHeader[2] == 0x44 && pdfHeader[3] == 0x46;
            }

            // CSV validation: enhanced heuristic with larger sample size (1KB)
            if (ext == ".csv")
            {
                const int sampleSize = 1024; // Read 1KB for validation
                int readSize = Math.Min(sampleSize, (int)Math.Min(fs.Length, int.MaxValue));
                Span<byte> buffer = stackalloc byte[sampleSize];
                int bytesRead = fs.Read(buffer[..readSize]);

                if (bytesRead == 0) return false;

                // Check for binary signatures that indicate non-text files
                // Look for null bytes, which are common in binary files
                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == 0x00)
                        return false; // Null byte indicates binary file
                }

                // Check that majority of bytes are text-like (ASCII printable + whitespace)
                int textLikeCount = 0;
                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = buffer[i];
                    // Allow: tab (0x09), newline (0x0A), carriage return (0x0D), printable ASCII (0x20-0x7E)
                    if (b == 0x09 || b == 0x0A || b == 0x0D || (b >= 0x20 && b <= 0x7E))
                        textLikeCount++;
                }

                // At least 90% of the sample should be text-like
                return textLikeCount >= (bytesRead * 0.9);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed MIME validation for {Path}", path);
            return false;
        }
        return false;
    }

    protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(backingStore, value))
            return false;

        backingStore = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}

/// <summary>
/// Information about a processed file
/// </summary>
public class ProcessedFileInfo : INotifyPropertyChanged
{
    private string _status = string.Empty;

    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ExecutionId { get; set; } = string.Empty;
    public int ChunkCount { get; set; }
    public int EmbeddingCount { get; set; }
    public string Metadata { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
