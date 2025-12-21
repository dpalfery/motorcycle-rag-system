using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;
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
    private readonly ILogger<DataPipelineOrchestrator> _logger;
    private readonly PipelineConfiguration _config;
    
    private readonly ConcurrentDictionary<string, PipelineExecutionResult> _runningExecutions;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens;

    public DataPipelineOrchestrator(
        IDataProcessor<CSVFile> csvProcessor,
        IDataProcessor<PDFDocument> pdfProcessor,
        IMotorcycleIndexingService indexingService,
        IPipelineMonitoringService monitoringService,
        IResilienceService resilienceService,
        ICorrelationService correlationService,
        IOptions<PipelineConfiguration> config,
        ILogger<DataPipelineOrchestrator> logger)
    {
        _csvProcessor = csvProcessor ?? throw new ArgumentNullException(nameof(csvProcessor));
        _pdfProcessor = pdfProcessor ?? throw new ArgumentNullException(nameof(pdfProcessor));
        _indexingService = indexingService ?? throw new ArgumentNullException(nameof(indexingService));
        _monitoringService = monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _correlationService = correlationService ?? throw new ArgumentNullException(nameof(correlationService));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _runningExecutions = new ConcurrentDictionary<string, PipelineExecutionResult>();
        _cancellationTokens = new ConcurrentDictionary<string, CancellationTokenSource>();
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
            Source = "API"
        };

        try
        {
            // Track the execution
            _runningExecutions[executionId] = result;
            var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationTokens[executionId] = cancellationSource;

            _logger.LogInformation("Starting pipeline execution {ExecutionId} for file {FileName} of type {FileType}",
                executionId, request.FileName, request.FileType);

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
            
            _logger.LogInformation("Pipeline execution {ExecutionId} was cancelled", executionId);
            return result;
        }
        catch (Exception ex)
        {
            result.Status = PipelineStatus.Failed;
            result.EndTime = DateTime.UtcNow;
            result.Errors.Add(ex.Message);
            result.Message = $"Pipeline execution failed: {ex.Message}";

            await _monitoringService.TrackPipelineFailureAsync(executionId, ex, executionContext);

            _logger.LogError(ex, "Pipeline execution {ExecutionId} failed", executionId);
            return result;
        }
        finally
        {
            // Cleanup
            _runningExecutions.TryRemove(executionId, out _);
            if (_cancellationTokens.TryRemove(executionId, out var cts))
            {
                cts?.Dispose();
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
        if (_runningExecutions.TryGetValue(executionId, out var result))
        {
            return result.Status;
        }

        // If not in memory, could query persistent storage here
        return PipelineStatus.Completed; // Assume completed if not found in running executions
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
        if (_cancellationTokens.TryGetValue(executionId, out var cts))
        {
            cts.Cancel();
            _logger.LogInformation("Pipeline execution {ExecutionId} cancellation requested", executionId);
            return true;
        }

        _logger.LogWarning("Pipeline execution {ExecutionId} not found for cancellation", executionId);
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