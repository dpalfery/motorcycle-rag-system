using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models;
using System.Collections.Concurrent;

namespace MotorcycleRAG.Application.Pipeline;

/// <summary>
/// Orchestrates ETL pipeline operations for motorcycle data processing
/// </summary>
public class DataPipelineOrchestrator : IDataPipelineOrchestrator
{
    private readonly IDataProcessor<CSVFile> _csvProcessor;
    private readonly IDataProcessor<PDFDocument> _pdfProcessor;
    private readonly IMotorcycleIndexingService _indexingService;
    private readonly IPipelineMonitoringService _monitoringService;
    private readonly IResilienceService _resilienceService;
    private readonly ICorrelationService _correlationService;
    private readonly IIngestionJobRepository _ingestionJobRepository;
    private readonly ILogger<DataPipelineOrchestrator> _logger;
    private readonly PipelineConfiguration _config;
    
    private readonly ConcurrentDictionary<string, PipelineExecutionResult> _runningExecutions;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens;
    private readonly object _cancellationLock = new();

    public DataPipelineOrchestrator(
        IDataProcessor<CSVFile> csvProcessor,
        IDataProcessor<PDFDocument> pdfProcessor,
        IMotorcycleIndexingService indexingService,
        IPipelineMonitoringService monitoringService,
        IResilienceService resilienceService,
        ICorrelationService correlationService,
        IIngestionJobRepository ingestionJobRepository,
        IOptions<PipelineConfiguration> config,
        ILogger<DataPipelineOrchestrator> logger)
    {
        _csvProcessor = csvProcessor ?? throw new ArgumentNullException(nameof(csvProcessor));
        _pdfProcessor = pdfProcessor ?? throw new ArgumentNullException(nameof(pdfProcessor));
        _indexingService = indexingService ?? throw new ArgumentNullException(nameof(indexingService));
        _monitoringService = monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _correlationService = correlationService ?? throw new ArgumentNullException(nameof(correlationService));
        _ingestionJobRepository = ingestionJobRepository ?? throw new ArgumentNullException(nameof(ingestionJobRepository));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _runningExecutions = new ConcurrentDictionary<string, PipelineExecutionResult>();
        _cancellationTokens = new ConcurrentDictionary<string, CancellationTokenSource>();
    }

    /// <summary>
    /// Sanitizes user-provided values for logging to prevent log injection attacks.
    /// Replaces newlines, carriage returns, and tabs with spaces.
    /// </summary>
    private string SanitizeForLogging(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return input
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace('\t', ' ');
    }

    public async Task<PipelineExecutionResult> ProcessFileAsync(DataPipelineRequest request, CancellationToken cancellationToken = default)
    {
        var executionId = Guid.NewGuid().ToString();
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        
        var result = new PipelineExecutionResult
        {
            ExecutionId = executionId,
            StartTime = DateTime.UtcNow,
            Status = PipelineStatus.Queued
        };

        var executionContext = new PipelineExecutionContext
        {
            CorrelationId = correlationId,
            RequestTime = result.StartTime,
            Source = request.CreatedBy ?? "API",
            UserId = request.CreatedBy ?? "System",
            Properties = new Dictionary<string, string>
            {
                ["FilePath"] = request.FilePath,
                ["FileName"] = request.FileName
            }
        };

        try
        {
            // Track the execution
            _runningExecutions[executionId] = result;
            var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationTokens[executionId] = cancellationSource;

            _logger.LogInformation("Starting pipeline execution {ExecutionId} for file {FileName} of type {FileType}",
                executionId, SanitizeForLogging(request.FileName), request.FileType);

            // Monitor pipeline start
            var pipelineType = request.FileType switch
            {
                FileType.CSV => PipelineType.CSV,
                FileType.PDF => PipelineType.PDF,
                _ => throw new ArgumentException($"Unsupported file type: {request.FileType}")
            };

            await _monitoringService.TrackPipelineStartAsync(executionId, pipelineType, executionContext);

            result.Status = PipelineStatus.Processing;
            result.Message = "Processing file...";

            // Process the file based on type
            ProcessedData processedData;
            
            processedData = await _resilienceService.ExecuteAsync(
                "process_file",
                async () =>
                {
                    return request.FileType switch
                    {
                        FileType.CSV => await ProcessCsvFileAsync(request, cancellationSource.Token),
                        FileType.PDF => await ProcessPdfFileAsync(request, cancellationSource.Token),
                        _ => throw new ArgumentException($"Unsupported file type: {request.FileType}")
                    };
                },
                correlationId: correlationId,
                cancellationToken: cancellationSource.Token);

            result.ProcessedData = processedData;
            result.Metrics["DocumentsProcessed"] = processedData.Documents.Count;
            result.Metrics["ProcessingDurationMs"] = (DateTime.UtcNow - result.StartTime).TotalMilliseconds;

            // Index the processed data if requested
            if (request.Options.IndexImmediately)
            {
                result.Status = PipelineStatus.Indexing;
                result.Message = "Indexing processed documents...";

                var indexingResult = await _resilienceService.ExecuteAsync(
                    "index_documents",
                    async () =>
                    {
                        return await _indexingService.IndexDocumentsAsync(processedData.Documents);
                    },
                    correlationId: correlationId,
                    cancellationToken: cancellationSource.Token);

                result.IndexingResult = new IndexingResult
                {
                    Success = indexingResult.Success,
                    DocumentsIndexed = indexingResult.DocumentsIndexed,
                    IndexName = "motorcycle_index",
                    Message = indexingResult.Success ? "Indexing completed successfully" : "Indexing completed with errors",
                    Errors = indexingResult.Errors?.ToList() ?? new List<string>(),
                    IndexingTime = indexingResult.ProcessingTime
                };

                result.Metrics["DocumentsIndexed"] = indexingResult.DocumentsProcessed;
            }

            result.Status = PipelineStatus.Completed;
            result.EndTime = DateTime.UtcNow;
            result.Message = $"Successfully processed {processedData.Documents.Count} documents";

            await _monitoringService.TrackPipelineCompletionAsync(executionId, result);

            _logger.LogInformation("Pipeline execution {ExecutionId} completed successfully in {Duration}ms",
                executionId, result.Duration.TotalMilliseconds);

            return result;
        }
        catch (OperationCanceledException)
        {
            result.Status = PipelineStatus.Cancelled;
            result.EndTime = DateTime.UtcNow;
            result.Message = "Pipeline execution was cancelled";
            
            // Update ingestion job status to Cancelled
            try
            {
                await _ingestionJobRepository.UpdateStatusAsync(
                    executionId,
                    IngestionJobStatus.Cancelled,
                    result.EndTime,
                    "Pipeline execution was cancelled by user request");
                
                _logger.LogInformation("Updated ingestion job {JobId} status to Cancelled", SanitizeForLogging(executionId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update ingestion job status for cancelled execution {ExecutionId}", SanitizeForLogging(executionId));
            }
            
            _logger.LogInformation("Pipeline execution {ExecutionId} was cancelled", SanitizeForLogging(executionId));
            return result;
        }
        catch (Exception ex)
        {
            result.Status = PipelineStatus.Failed;
            result.EndTime = DateTime.UtcNow;
            result.Errors.Add(ex.Message);
            result.Message = $"Pipeline execution failed: {ex.Message}";

            await _monitoringService.TrackPipelineFailureAsync(executionId, ex, executionContext);

            _logger.LogError(ex, "Pipeline execution {ExecutionId} failed", SanitizeForLogging(executionId));
            return result;
        }
        finally
        {
            // Cleanup - synchronize with CancelPipelineAsync
            lock (_cancellationLock)
            {
                _runningExecutions.TryRemove(executionId, out _);
                if (_cancellationTokens.TryRemove(executionId, out var cts))
                {
                    try
                    {
                        cts?.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Already disposed, ignore
                    }
                }
            }
        }
    }

    public async Task<BatchPipelineResult> ProcessBatchAsync(IEnumerable<DataPipelineRequest> requests, CancellationToken cancellationToken = default)
    {
        var batchResult = new BatchPipelineResult
        {
            StartTime = DateTime.UtcNow
        };

        var requestList = requests.ToList();
        batchResult.TotalFiles = requestList.Count;

        _logger.LogInformation("Starting batch pipeline processing for {FileCount} files", batchResult.TotalFiles);

        try
        {
            // Process files concurrently with max concurrency limit
            var semaphore = new SemaphoreSlim(_config.MaxConcurrentExecutions, _config.MaxConcurrentExecutions);
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
            });

            var results = await Task.WhenAll(tasks);
            batchResult.Results = results.ToList();

            // Calculate summary statistics
            batchResult.ProcessedSuccessfully = results.Count(r => r.Status == PipelineStatus.Completed);
            batchResult.ProcessedWithErrors = results.Count(r => r.Status == PipelineStatus.PartiallyCompleted);
            batchResult.Failed = results.Count(r => r.Status == PipelineStatus.Failed);

            batchResult.BatchMetrics["TotalDocumentsProcessed"] = results.Sum(r => r.ProcessedData?.Documents.Count ?? 0);
            batchResult.BatchMetrics["TotalDocumentsIndexed"] = results.Sum(r => r.IndexingResult?.DocumentsIndexed ?? 0);
            batchResult.BatchMetrics["AverageProcessingTimeMs"] = results.Average(r => r.Duration.TotalMilliseconds);

            batchResult.EndTime = DateTime.UtcNow;

            _logger.LogInformation("Batch pipeline processing completed. Success: {Success}, Failed: {Failed}, Duration: {Duration}ms",
                batchResult.ProcessedSuccessfully, batchResult.Failed, (batchResult.EndTime - batchResult.StartTime).Value.TotalMilliseconds);

            return batchResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch pipeline processing failed");
            batchResult.EndTime = DateTime.UtcNow;
            throw;
        }
    }

    public async Task<PipelineStatus> GetPipelineStatusAsync(string executionId)
    {
        // Check in-memory running executions first
        if (_runningExecutions.TryGetValue(executionId, out var result))
        {
            return result.Status;
        }

        // Query persistent storage for completed/cancelled/failed jobs
        try
        {
            var job = await _ingestionJobRepository.GetByJobIdAsync(executionId);
            if (job != null)
            {
                return MapIngestionJobStatusToPipelineStatus(job.Status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query ingestion job status for execution {ExecutionId}", SanitizeForLogging(executionId));
        }

        return PipelineStatus.Completed; // Default fallback
    }

    public async Task<PipelineMetrics> GetPipelineMetricsAsync(TimeSpan? timeWindow = null)
    {
        var window = timeWindow ?? TimeSpan.FromHours(24);
        return await _monitoringService.GetDetailedMetricsAsync(window).ContinueWith(t => 
        {
            var detailed = t.Result;
            return new PipelineMetrics
            {
                TotalExecutions = detailed.Executions.TotalExecutions,
                SuccessfulExecutions = detailed.Executions.SuccessfulExecutions,
                FailedExecutions = detailed.Executions.FailedExecutions,
                AverageExecutionTime = detailed.Performance.AverageExecutionTime,
                TotalDocumentsProcessed = detailed.Processing.TotalDocumentsProcessed,
                TotalDocumentsIndexed = detailed.Processing.TotalDocumentsIndexed,
                MetricsStartTime = detailed.StartTime,
                MetricsEndTime = detailed.EndTime,
                ProcessingByFileType = detailed.Processing.DocumentsByType.ToDictionary(k => k.Key, v => (int)v.Value),
                ExecutionsByStatus = new Dictionary<PipelineStatus, int>
                {
                    [PipelineStatus.Completed] = detailed.Executions.SuccessfulExecutions,
                    [PipelineStatus.Failed] = detailed.Executions.FailedExecutions,
                    [PipelineStatus.Cancelled] = detailed.Executions.CancelledExecutions
                }
            };
        });
    }

    public async Task<bool> CancelPipelineAsync(string executionId)
    {
        CancellationTokenSource? ctsToCancel = null;
        
        // Synchronize access to prevent race conditions with finally block cleanup
        lock (_cancellationLock)
        {
            if (_cancellationTokens.TryRemove(executionId, out var cts))
            {
                ctsToCancel = cts;
            }
        }

        if (ctsToCancel != null)
        {
            // Store cancellation request in persistent storage
            try
            {
                // Check current job status before updating to prevent overwriting terminal states
                var job = await _ingestionJobRepository.GetByJobIdAsync(executionId);
                if (job != null && job.Status != IngestionJobStatus.Completed &&
                    job.Status != IngestionJobStatus.Failed &&
                    job.Status != IngestionJobStatus.Cancelled)
                {
                    await _ingestionJobRepository.UpdateStatusAsync(
                        executionId,
                        IngestionJobStatus.Cancelled,
                        DateTime.UtcNow,
                        "Cancellation requested by user");
                    
                    _logger.LogInformation("Stored cancellation request for pipeline execution {ExecutionId} in persistent storage", SanitizeForLogging(executionId));
                }
                else
                {
                    _logger.LogInformation("Pipeline execution {ExecutionId} is already in terminal state {Status}, skipping status update",
                        SanitizeForLogging(executionId), job?.Status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store cancellation request for execution {ExecutionId}", SanitizeForLogging(executionId));
                // Continue with cancellation even if persistence fails
            }

            // Cancel the cancellation token to stop ongoing processing
            try
            {
                ctsToCancel.Cancel();
                _logger.LogInformation("Pipeline execution {ExecutionId} cancellation requested and token cancelled", SanitizeForLogging(executionId));
            }
            catch (ObjectDisposedException)
            {
                // CTS was already disposed, cancellation already in progress or completed
                _logger.LogWarning("Cancellation token for pipeline execution {ExecutionId} was already disposed", SanitizeForLogging(executionId));
            }
            finally
            {
                // Dispose safely
                try
                {
                    ctsToCancel.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, ignore
                }
            }
            
            return true;
        }

        // Check if the job exists in persistent storage (might be in a state that can still be cancelled)
        try
        {
            var job = await _ingestionJobRepository.GetByJobIdAsync(executionId);
            if (job != null && (job.Status == IngestionJobStatus.Queued || job.Status == IngestionJobStatus.Processing || job.Status == IngestionJobStatus.Indexing))
            {
                // Job exists and is in a cancellable state - double-check it hasn't reached terminal state
                var currentJob = await _ingestionJobRepository.GetByJobIdAsync(executionId);
                if (currentJob != null && currentJob.Status != IngestionJobStatus.Completed &&
                    currentJob.Status != IngestionJobStatus.Failed &&
                    currentJob.Status != IngestionJobStatus.Cancelled)
                {
                    await _ingestionJobRepository.UpdateStatusAsync(
                        executionId,
                        IngestionJobStatus.Cancelled,
                        DateTime.UtcNow,
                        "Cancellation requested by user");
                    
                    _logger.LogInformation("Marked pipeline execution {ExecutionId} as Cancelled in persistent storage", SanitizeForLogging(executionId));
                    return true;
                }
                else
                {
                    _logger.LogInformation("Pipeline execution {ExecutionId} is already in terminal state {Status}, cannot cancel",
                        SanitizeForLogging(executionId), currentJob?.Status);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check/cancel pipeline execution {ExecutionId} in persistent storage", SanitizeForLogging(executionId));
        }

        _logger.LogWarning("Pipeline execution {ExecutionId} not found or not in a cancellable state", SanitizeForLogging(executionId));
        return false;
    }

    private async Task<ProcessedData> ProcessCsvFileAsync(DataPipelineRequest request, CancellationToken cancellationToken)
    {
        var csvFile = new CSVFile
        {
            FileName = request.FileName,
            Content = File.OpenRead(request.FilePath),
            HasHeaders = true,
            MaxColumns = 100,
            Delimiter = ",",
            Encoding = "UTF-8"
        };

        return await _csvProcessor.ProcessAsync(csvFile);
    }

    private async Task<ProcessedData> ProcessPdfFileAsync(DataPipelineRequest request, CancellationToken cancellationToken)
    {
        var pdfDocument = new PDFDocument
        {
            FileName = request.FileName,
            Content = File.OpenRead(request.FilePath),
            ContainsImages = true
        };

        return await _pdfProcessor.ProcessAsync(pdfDocument);
    }
    /// <summary>
    /// Maps IngestionJobStatus to PipelineStatus
    /// </summary>
    private static PipelineStatus MapIngestionJobStatusToPipelineStatus(IngestionJobStatus jobStatus)
    {
        return jobStatus switch
        {
            IngestionJobStatus.Queued => PipelineStatus.Queued,
            IngestionJobStatus.Processing => PipelineStatus.Processing,
            IngestionJobStatus.Indexing => PipelineStatus.Indexing,
            IngestionJobStatus.Completed => PipelineStatus.Completed,
            IngestionJobStatus.Failed => PipelineStatus.Failed,
            IngestionJobStatus.Cancelled => PipelineStatus.Cancelled,
            IngestionJobStatus.PartiallyCompleted => PipelineStatus.PartiallyCompleted,
            _ => PipelineStatus.Completed
        };
    }
}

/// <summary>
/// Configuration for pipeline orchestration
/// </summary>
public class PipelineConfiguration
{
    public int MaxConcurrentExecutions { get; set; } = 3;
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public int MaxRetries { get; set; } = 3;
    public string TempDirectory { get; set; } = "temp";
    public bool EnableDetailedLogging { get; set; } = true;
}
