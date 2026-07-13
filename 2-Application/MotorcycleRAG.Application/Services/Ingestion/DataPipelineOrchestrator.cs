using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Orchestrates the data pipeline for processing and indexing motorcycle data files.
/// Coordinates between file upload, processing, and indexing services.
/// </summary>
#pragma warning disable S1200 // Classes should be small - orchestrator coordinates multiple services by design
public partial class DataPipelineOrchestrator : IDataPipelineOrchestrator
{
    private readonly IDataProcessor<PDFDocument> _pdfProcessor;
    private readonly IDataProcessor<CSVFile> _csvProcessor;
    private readonly IFileUploadService _fileUploadService;
    private readonly ILocalFileStore _localFileStore;
    private readonly IAzureSearchDocumentService _searchService;
    private readonly ILogger<DataPipelineOrchestrator> _logger;
    private readonly PipelineConfiguration _config;

    // In-memory tracking for pipeline executions (would be replaced with repository in production)
    private readonly ConcurrentDictionary<string, PipelineExecutionResult> _executions = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();

    public DataPipelineOrchestrator(
        IDataProcessor<PDFDocument> pdfProcessor,
        IDataProcessor<CSVFile> csvProcessor,
        IFileUploadService fileUploadService,
        ILocalFileStore localFileStore,
        IAzureSearchDocumentService searchService,
        IOptions<PipelineConfiguration> config,
        ILogger<DataPipelineOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(pdfProcessor);
        ArgumentNullException.ThrowIfNull(csvProcessor);
        ArgumentNullException.ThrowIfNull(fileUploadService);
        ArgumentNullException.ThrowIfNull(localFileStore);
        ArgumentNullException.ThrowIfNull(searchService);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _pdfProcessor = pdfProcessor;
        _csvProcessor = csvProcessor;
        _fileUploadService = fileUploadService;
        _localFileStore = localFileStore;
        _searchService = searchService;
        _config = config.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PipelineExecutionResult> ProcessFileAsync(
        DataPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executionId = Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();

        var result = new PipelineExecutionResult
        {
            ExecutionId = executionId,
            StartTime = DateTime.UtcNow,
            Status = PipelineStatus.Processing
        };

        _executions[executionId] = result;

        // Create a linked CancellationTokenSource for cancellation support
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokens[executionId] = cts;

        try
        {
            LogStartingPipelineExecution(executionId, request.FileName, request.FileType);

            // Check for cancellation
            if (cts.Token.IsCancellationRequested)
            {
                result.Status = PipelineStatus.Cancelled;
                result.Message = "Pipeline execution was cancelled before processing started.";
                return result;
            }

            // Read and validate the file
            var (stream, validationError) = await ReadFileAsync(request, cts.Token);
            if (validationError != null)
            {
                result.Status = PipelineStatus.Failed;
                result.Errors.Add(validationError);
                result.Message = $"File validation failed: {validationError}";
                return result;
            }

            // Stream is guaranteed to be non-null when validationError is null
            if (stream == null)
            {
                result.Status = PipelineStatus.Failed;
                result.Message = "Failed to read file: stream is null.";
                return result;
            }

            // Check for cancellation
            if (cts.Token.IsCancellationRequested)
            {
                result.Status = PipelineStatus.Cancelled;
                result.Message = "Pipeline execution was cancelled after file reading.";
                await DisposeStreamAsync(stream);
                return result;
            }

            // Process based on file type
            ProcessedData? processedData = request.FileType switch
            {
                FileType.PDF => await ProcessPdfFileAsync(request, stream, cts.Token),
                FileType.CSV => await ProcessCsvFileAsync(request, stream, cts.Token),
                _ => throw new NotSupportedException($"File type {request.FileType} is not supported")
            };

            if (processedData == null)
            {
                result.Status = PipelineStatus.Failed;
                result.Message = "Processing returned no data.";
                await DisposeStreamAsync(stream);
                return result;
            }

            result.ProcessedData = processedData;
            result.Metrics["DocumentsExtracted"] = processedData.Documents.Count;

            LogDocumentsExtracted(processedData.Documents.Count, request.FileName);

            // Check for cancellation before indexing
            if (cts.Token.IsCancellationRequested)
            {
                result.Status = PipelineStatus.Cancelled;
                result.Message = "Pipeline execution was cancelled after processing.";
                await DisposeStreamAsync(stream);
                return result;
            }

            // Index documents if enabled and documents exist
            if (_config.EnableAutoIndexing && request.Options.IndexImmediately && processedData.Documents.Count > 0)
            {
                result.Status = PipelineStatus.Indexing;
                
                var indexingResult = await IndexDocumentsAsync(processedData.Documents, cts.Token);
                result.IndexingResult = indexingResult;
                result.Metrics["DocumentsIndexed"] = indexingResult.DocumentsIndexed;

                if (!indexingResult.Success)
                {
                    result.Status = PipelineStatus.PartiallyCompleted;
                    result.Message = $"Processing completed but indexing failed: {indexingResult.Message}";
                    foreach (var error in indexingResult.Errors)
                    {
                        result.Errors.Add(error);
                    }
                }
                else
                {
                    result.Status = PipelineStatus.Completed;
                    result.Message = $"Successfully processed and indexed {indexingResult.DocumentsIndexed} documents.";
                }
            }
            else
            {
                result.Status = PipelineStatus.Completed;
                result.Message = $"Successfully processed {processedData.Documents.Count} documents. Indexing skipped.";
            }

            LogPipelineExecutionCompleted(executionId, result.Status);
            await DisposeStreamAsync(stream);
        }
        catch (OperationCanceledException)
        {
            result.Status = PipelineStatus.Cancelled;
            result.Message = "Pipeline execution was cancelled.";
            LogPipelineExecutionCancelled(executionId);
        }
        catch (Exception ex)
        {
            result.Status = PipelineStatus.Failed;
            result.Message = $"Pipeline execution failed: {ex.Message}";
            result.Errors.Add(ex.Message);
            LogPipelineExecutionFailed(executionId, request.FileName, ex);
        }
        finally
        {
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Metrics["ProcessingTimeMs"] = stopwatch.ElapsedMilliseconds;
            result.Metrics["FileExtension"] = Path.GetExtension(request.FileName) ?? "unknown";

            // Cleanup cancellation token
            _cancellationTokens.TryRemove(executionId, out _);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<BatchPipelineResult> ProcessBatchAsync(
        IEnumerable<DataPipelineRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var requestList = requests.ToList();
        var batchId = Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();

        LogStartingBatchProcessing(batchId, requestList.Count);

        var result = new BatchPipelineResult
        {
            BatchId = batchId,
            StartTime = DateTime.UtcNow,
            TotalFiles = requestList.Count
        };

        // Process files with limited concurrency
        using var semaphore = new SemaphoreSlim(_config.MaxConcurrentProcessing, _config.MaxConcurrentProcessing);
        var tasks = requestList.Select(async request =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await ProcessFileAsync(request, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var completedResults = await Task.WhenAll(tasks);

        // Aggregate results
        foreach (var executionResult in completedResults)
        {
            result.Results.Add(executionResult);

            switch (executionResult.Status)
            {
                case PipelineStatus.Completed:
                    result.ProcessedSuccessfully++;
                    break;
                case PipelineStatus.PartiallyCompleted:
                    result.ProcessedWithErrors++;
                    break;
                default:
                    result.Failed++;
                    break;
            }
        }

        stopwatch.Stop();
        result.EndTime = DateTime.UtcNow;
        result.BatchMetrics["TotalProcessingTimeMs"] = stopwatch.ElapsedMilliseconds;
        result.BatchMetrics["AverageProcessingTimeMs"] = 
            requestList.Count > 0 ? stopwatch.ElapsedMilliseconds / requestList.Count : 0;

        LogBatchProcessingCompleted(batchId, result.ProcessedSuccessfully, result.ProcessedWithErrors, result.Failed);

        return result;
    }

    /// <inheritdoc />
    public Task<PipelineStatus> GetPipelineStatusAsync(string executionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        if (_executions.TryGetValue(executionId, out var result))
        {
            return Task.FromResult(result.Status);
        }

        LogPipelineExecutionNotFound(executionId);
        return Task.FromResult(PipelineStatus.Failed);
    }

    /// <inheritdoc />
    public Task<PipelineMetrics> GetPipelineMetricsAsync(TimeSpan? timeWindow = null)
    {
        var cutoff = timeWindow.HasValue
            ? DateTime.UtcNow.Subtract(timeWindow.Value)
            : DateTime.MinValue;

        var relevantExecutions = _executions.Values
            .Where(e => e.StartTime >= cutoff)
            .ToList();

        var metrics = new PipelineMetrics
        {
            MetricsStartTime = relevantExecutions.Count != 0 ? relevantExecutions.Min(e => e.StartTime) : DateTime.MinValue,
            MetricsEndTime = relevantExecutions.Count != 0 ? relevantExecutions.Max(e => e.EndTime) ?? DateTime.UtcNow : DateTime.UtcNow,
            TotalExecutions = relevantExecutions.Count,
            SuccessfulExecutions = relevantExecutions.Count(e => e.Status == PipelineStatus.Completed),
            FailedExecutions = relevantExecutions.Count(e => e.Status == PipelineStatus.Failed),
            TotalDocumentsProcessed = relevantExecutions
                .Where(e => e.ProcessedData != null)
                .Sum(e => e.ProcessedData!.Documents.Count),
            TotalDocumentsIndexed = relevantExecutions
                .Where(e => e.IndexingResult != null)
                .Sum(e => e.IndexingResult!.DocumentsIndexed),
            AverageExecutionTime = relevantExecutions.Count != 0
                ? TimeSpan.FromMilliseconds(relevantExecutions.Average(e => e.Duration.TotalMilliseconds))
                : TimeSpan.Zero
        };

        // Group by file type
        var byFileType = relevantExecutions
            .Where(e => e.Metrics.ContainsKey("FileExtension"))
            .GroupBy(e => (string)e.Metrics["FileExtension"])
            .ToDictionary(
                g => g.Key.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? FileType.PDF : FileType.CSV,
                g => g.Count());

        foreach (var kvp in byFileType)
        {
            metrics.ProcessingByFileType[kvp.Key] = kvp.Value;
        }

        // Group by status
        var byStatus = relevantExecutions
            .GroupBy(e => e.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var kvp in byStatus)
        {
            metrics.ExecutionsByStatus[kvp.Key] = kvp.Value;
        }

        foreach (var execution in relevantExecutions
                     .OrderByDescending(e => e.StartTime)
                     .Take(50))
        {
            metrics.RecentExecutions.Add(new PipelineExecutionSummary
            {
                ExecutionId = execution.ExecutionId,
                FileName = execution.Metrics.TryGetValue("OriginalFileName", out var originalFileName)
                    ? originalFileName?.ToString() ?? string.Empty
                    : string.Empty,
                FileType = execution.Metrics.TryGetValue("FileExtension", out var fileExtension)
                           && string.Equals(fileExtension?.ToString(), ".pdf", StringComparison.OrdinalIgnoreCase)
                    ? FileType.PDF
                    : FileType.CSV,
                Status = execution.Status,
                StartTime = execution.StartTime,
                Duration = execution.Duration,
                DocumentsProcessed = execution.ProcessedData?.Documents.Count ?? 0,
                DocumentsIndexed = execution.IndexingResult?.DocumentsIndexed ?? 0,
                ErrorMessage = execution.Errors.FirstOrDefault() ?? string.Empty
            });
        }

        return Task.FromResult(metrics);
    }

    /// <inheritdoc />
    public async Task<bool> CancelPipelineAsync(string executionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        if (_cancellationTokens.TryGetValue(executionId, out var cts))
        {
            LogCancellingPipelineExecution(executionId);
            await cts.CancelAsync().ConfigureAwait(false);
            return true;
        }

        LogPipelineExecutionNotFoundForCancellation(executionId);
        return false;
    }

    /// <summary>
    /// Reads a file from the specified path in the request.
    /// </summary>
    private async Task<(Stream? Stream, string? Error)> ReadFileAsync(
        DataPipelineRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var fileBytes = await _localFileStore
                .ReadAllBytesIfExistsAsync(request.FilePath, cancellationToken)
                .ConfigureAwait(false);
            if (fileBytes is null)
            {
                return (null, $"File not found at path: {request.FilePath}");
            }

            var stream = new MemoryStream(fileBytes);

            // Validate using file upload service
            var metadata = new FileMetadata
            {
                FileName = request.FileName,
                ContentType = GetContentType(request.FileType),
                ContentLength = fileBytes.Length
            };

            var options = new FileUploadOptions();
            var validationResult = await _fileUploadService.ValidateFileAsync(stream, metadata, options).ConfigureAwait(false);

            if (!validationResult.IsValid)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                var errors = string.Join("; ", validationResult.Errors);
                return (null, errors);
            }

            // Reset stream position for processing
            stream.Position = 0;
            return (stream, null);
        }
        catch (Exception ex)
        {
            LogFileReadError(request.FilePath, ex);
            return (null, $"Failed to read file: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes a PDF file through the PDF processor.
    /// </summary>
#pragma warning disable S1172 // Unused parameter - cancellationToken reserved for future use when processors support cancellation
    private async Task<ProcessedData?> ProcessPdfFileAsync(
        DataPipelineRequest request,
        Stream stream,
        CancellationToken cancellationToken)
#pragma warning restore S1172
    {
        // Extract metadata from request
        var make = request.Metadata.TryGetValue("Make", out var makeObj) ? makeObj?.ToString() ?? string.Empty : string.Empty;
        var model = request.Metadata.TryGetValue("Model", out var modelObj) ? modelObj?.ToString() ?? string.Empty : string.Empty;
        var year = request.Metadata.TryGetValue("Year", out var yearObj) ? yearObj?.ToString() ?? string.Empty : string.Empty;
        var source = request.Metadata.TryGetValue("Source", out var sourceObj) ? sourceObj?.ToString() ?? string.Empty : request.FilePath;

        var pdfDocument = new PDFDocument
        {
            FileName = request.FileName,
            Content = stream,
            DocumentType = DeterminePdfDocumentType(request.Metadata),
            Make = make,
            Model = model,
            Year = year,
            Source = source,
            FileSizeBytes = stream.Length,
            UploadedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>(request.Metadata)
        };

        LogProcessingPdfDocument(pdfDocument.FileName, pdfDocument.DocumentType);

        return await _pdfProcessor.ProcessAsync(pdfDocument).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes a CSV file through the CSV processor.
    /// </summary>
    private async Task<ProcessedData?> ProcessCsvFileAsync(
        DataPipelineRequest request,
        Stream stream,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken; // Reserved for future use if processors need cancellation

        var source = request.Metadata.TryGetValue("Source", out var sourceObj)
            ? sourceObj?.ToString() ?? string.Empty
            : request.FilePath;

        var csvFile = new CSVFile
        {
            FileName = request.FileName,
            Content = stream,
            Size = stream.Length,
            Source = source,
            UploadedAt = DateTime.UtcNow,
            HasHeaders = true,
            Delimiter = ",",
            Encoding = "UTF-8",
            Metadata = request.Metadata.ToDictionary(k => k.Key, k => k.Value?.ToString() ?? string.Empty)
        };

        LogProcessingCsvFile(csvFile.FileName);

        return await _csvProcessor.ProcessAsync(csvFile).ConfigureAwait(false);
    }

    /// <summary>
    /// Indexes processed documents to Azure AI Search.
    /// </summary>
    private async Task<IndexingResult> IndexDocumentsAsync(
        ICollection<MotorcycleDocument> documents,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new IndexingResult
        {
            Success = false,
            Message = "Indexing not attempted",
            // Legacy full-document path has no per-document category; route the label through the
            // canonical naming convention (category-aware routing lives in ChunkIndexingService).
            IndexName = MotorcycleSearchIndexNaming.DefaultIndexName
        };

        try
        {
            LogIndexingDocuments(documents.Count);

            var success = await _searchService.IndexDocumentsAsync(documents.ToArray(), cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            result.IndexingTime = stopwatch.Elapsed;
            result.DocumentsIndexed = documents.Count;

            if (success)
            {
                result.Success = true;
                result.Message = $"Successfully indexed {documents.Count} documents.";
                LogIndexingSucceeded(documents.Count, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                result.Message = "Indexing operation returned false.";
                result.Errors.Add("Azure AI Search indexing failed.");
                LogIndexingReturnedFalse(documents.Count);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.IndexingTime = stopwatch.Elapsed;
            result.Message = $"Indexing failed: {ex.Message}";
            result.Errors.Add(ex.Message);
            LogIndexingFailed(documents.Count, ex);
        }

        return result;
    }

    /// <summary>
    /// Safely disposes a stream asynchronously.
    /// </summary>
    private static async Task DisposeStreamAsync(Stream? stream)
    {
        if (stream != null)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Determines the PDF document type from metadata.
    /// </summary>
    private static PdfDocumentType DeterminePdfDocumentType(Dictionary<string, object> metadata)
    {
        if (metadata.TryGetValue("DocumentType", out var docTypeObj) &&
            docTypeObj is string docType &&
            Enum.TryParse<PdfDocumentType>(docType, ignoreCase: true, out var parsedType))
        {
            return parsedType;
        }

        return PdfDocumentType.Manual;
    }

    /// <summary>
    /// Gets the content type for a file type.
    /// </summary>
    private static string GetContentType(FileType fileType) => fileType switch
    {
        FileType.PDF => "application/pdf",
        FileType.CSV => "text/csv",
        _ => "application/octet-stream"
    };

    #region Logging Methods

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting pipeline execution {ExecutionId} for file {FileName} of type {FileType}")]
    private partial void LogStartingPipelineExecution(string executionId, string fileName, FileType fileType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted {DocumentCount} documents from {FileName}")]
    private partial void LogDocumentsExtracted(int documentCount, string fileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pipeline execution {ExecutionId} completed with status {Status}")]
    private partial void LogPipelineExecutionCompleted(string executionId, PipelineStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pipeline execution {ExecutionId} was cancelled")]
    private partial void LogPipelineExecutionCancelled(string executionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Pipeline execution {ExecutionId} failed for file {FileName}")]
    private partial void LogPipelineExecutionFailed(string executionId, string fileName, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting batch processing {BatchId} with {Count} files")]
    private partial void LogStartingBatchProcessing(string batchId, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch processing {BatchId} completed: {Success} successful, {Partial} partial, {Failed} failed")]
    private partial void LogBatchProcessingCompleted(string batchId, int success, int partial, int failed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pipeline execution {ExecutionId} not found")]
    private partial void LogPipelineExecutionNotFound(string executionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cancelling pipeline execution {ExecutionId}")]
    private partial void LogCancellingPipelineExecution(string executionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pipeline execution {ExecutionId} not found for cancellation")]
    private partial void LogPipelineExecutionNotFoundForCancellation(string executionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to read file {FilePath}")]
    private partial void LogFileReadError(string filePath, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing PDF document {FileName} with type {DocumentType}")]
    private partial void LogProcessingPdfDocument(string fileName, PdfDocumentType documentType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing CSV file {FileName}")]
    private partial void LogProcessingCsvFile(string fileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexing {Count} documents to Azure AI Search")]
    private partial void LogIndexingDocuments(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully indexed {Count} documents in {Time}ms")]
    private partial void LogIndexingSucceeded(int count, long time);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Indexing returned false for {Count} documents")]
    private partial void LogIndexingReturnedFalse(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to index {Count} documents")]
    private partial void LogIndexingFailed(int count, Exception exception);

    #endregion
}
#pragma warning restore S1200
