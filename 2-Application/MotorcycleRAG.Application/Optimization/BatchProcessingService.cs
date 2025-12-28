using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using MotorcycleRAG.Contracts.Optimization;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Application.Optimization;

/// <summary>
/// Implementation of optimized batch processing for data ingestion operations.
/// </summary>
public class BatchProcessingService : IBatchProcessingService
{
    private readonly ILogger<BatchProcessingService> _logger;
    private readonly object _statsLock = new();
    private BatchProcessingStatistics _statistics = new();

    public BatchProcessingService(ILogger<BatchProcessingService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BatchProcessingResult<TResult>> ProcessBatchAsync<T, TResult>(
        IEnumerable<T> documents,
        Func<IEnumerable<T>, CancellationToken, Task<IEnumerable<TResult>>> processor,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (documents == null)
            throw new ArgumentNullException(nameof(documents));
        if (processor == null)
            throw new ArgumentNullException(nameof(processor));

        var documentList = documents.ToList();
        var totalCount = documentList.Count;
        
        if (totalCount == 0)
        {
            return new BatchProcessingResult<TResult>
            {
                Results = Array.Empty<TResult>(),
                TotalProcessed = 0,
                SuccessfullyProcessed = 0
            };
        }

        _logger.LogInformation("Starting batch processing of {TotalCount} documents with batch size {BatchSize}", 
            totalCount, batchSize);

        var stopwatch = Stopwatch.StartNew();
        var allResults = new List<TResult>();
        var allErrors = new List<BatchProcessingError>();
        var processedCount = 0;

        try
        {
            for (int i = 0; i < totalCount; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = documentList.Skip(i).Take(batchSize);
                var batchNumber = (i / batchSize) + 1;
                var totalBatches = (totalCount + batchSize - 1) / batchSize;

                _logger.LogDebug("Processing batch {BatchNumber}/{TotalBatches}", batchNumber, totalBatches);

                try
                {
                    var batchResults = await processor(batch, cancellationToken);
                    allResults.AddRange(batchResults);
                    processedCount += batch.Count();

                    _logger.LogDebug("Batch {BatchNumber} completed successfully with {ResultCount} results", 
                        batchNumber, batchResults.Count());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing batch {BatchNumber}", batchNumber);
                    
                    // Add error for each item in the failed batch
                    var batchList = batch.ToList();
                    for (int j = 0; j < batchList.Count; j++)
                    {
                        allErrors.Add(new BatchProcessingError
                        {
                            ItemIndex = i + j,
                            ItemId = $"batch-{batchNumber}-item-{j}",
                            Exception = ex,
                            ErrorMessage = ex.Message
                        });
                    }
                }
            }

            stopwatch.Stop();

            var result = new BatchProcessingResult<TResult>
            {
                Results = allResults,
                Errors = allErrors,
                TotalProcessed = totalCount,
                SuccessfullyProcessed = processedCount,
                Failed = totalCount - processedCount,
                TotalDuration = stopwatch.Elapsed
            };

            // Update statistics
            UpdateStatistics(result, batchSize);

            _logger.LogInformation("Batch processing completed: {Processed}/{Total} successful, {Failed} failed, Duration: {Duration}ms, Throughput: {Throughput:F2}/sec",
                result.SuccessfullyProcessed, result.TotalProcessed, result.Failed, 
                result.TotalDuration.TotalMilliseconds, result.ThroughputPerSecond);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Fatal error during batch processing");
            throw;
        }
    }

    public async Task<BatchProcessingResult<TResult>> ProcessParallelBatchAsync<T, TResult>(
        IEnumerable<T> documents,
        Func<T, CancellationToken, Task<TResult>> processor,
        BatchProcessingOptions options,
        CancellationToken cancellationToken = default)
    {
        if (documents == null)
            throw new ArgumentNullException(nameof(documents));
        if (processor == null)
            throw new ArgumentNullException(nameof(processor));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        var documentList = documents.ToList();
        var totalCount = documentList.Count;

        if (totalCount == 0)
        {
            return new BatchProcessingResult<TResult>
            {
                Results = Array.Empty<TResult>(),
                TotalProcessed = 0,
                SuccessfullyProcessed = 0
            };
        }

        _logger.LogInformation("Starting parallel batch processing of {TotalCount} documents with {Parallelism} degree of parallelism", 
            totalCount, options.MaxDegreeOfParallelism);

        var stopwatch = Stopwatch.StartNew();
        var results = new ConcurrentBag<TResult>();
        var errors = new ConcurrentBag<BatchProcessingError>();
        var processedCount = 0;

        try
        {
            var semaphore = new SemaphoreSlim(options.MaxDegreeOfParallelism, options.MaxDegreeOfParallelism);
            var tasks = new List<Task>();

            for (int i = 0; i < totalCount; i++)
            {
                var index = i;
                var document = documentList[i];

                var task = ProcessItemWithSemaphoreAsync(
                    document, processor, semaphore, options, 
                    index, results, errors, cancellationToken);
                
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            processedCount = results.Count;

            stopwatch.Stop();

            var result = new BatchProcessingResult<TResult>
            {
                Results = results.ToList(),
                Errors = errors.ToList(),
                TotalProcessed = totalCount,
                SuccessfullyProcessed = processedCount,
                Failed = totalCount - processedCount,
                TotalDuration = stopwatch.Elapsed
            };

            // Update statistics
            UpdateStatistics(result, options.BatchSize);

            _logger.LogInformation("Parallel batch processing completed: {Processed}/{Total} successful, {Failed} failed, Duration: {Duration}ms, Throughput: {Throughput:F2}/sec",
                result.SuccessfullyProcessed, result.TotalProcessed, result.Failed, 
                result.TotalDuration.TotalMilliseconds, result.ThroughputPerSecond);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Fatal error during parallel batch processing");
            throw;
        }
    }

    public int OptimizeBatchSize(int documentCount, long averageDocumentSize, long availableMemory)
    {
        if (documentCount <= 0 || averageDocumentSize <= 0 || availableMemory <= 0)
            return 100; // Default batch size

        // Calculate optimal batch size based on memory constraints
        var memoryPerDocument = averageDocumentSize * 3; // Account for processing overhead
        var maxBatchSizeByMemory = (int)(availableMemory * 0.8 / memoryPerDocument); // Use 80% of available memory

        // Consider processing efficiency
        var optimalBatchSize = documentCount switch
        {
            < 100 => Math.Min(documentCount, 10),
            < 1000 => Math.Min(documentCount / 10, 100),
            < 10000 => Math.Min(documentCount / 50, 500),
            _ => Math.Min(documentCount / 100, 1000)
        };

        // Take the minimum of memory-constrained and efficiency-optimized batch sizes
        var finalBatchSize = Math.Min(maxBatchSizeByMemory, optimalBatchSize);
        
        // Ensure minimum batch size of 1
        finalBatchSize = Math.Max(1, finalBatchSize);

        _logger.LogDebug("Optimized batch size: {BatchSize} (Documents: {DocumentCount}, AvgSize: {AvgSize}B, Memory: {Memory}B)",
            finalBatchSize, documentCount, averageDocumentSize, availableMemory);

        return finalBatchSize;
    }

    public BatchProcessingStatistics GetStatistics()
    {
        lock (_statsLock)
        {
            return new BatchProcessingStatistics
            {
                TotalBatchesProcessed = _statistics.TotalBatchesProcessed,
                TotalItemsProcessed = _statistics.TotalItemsProcessed,
                TotalItemsFailed = _statistics.TotalItemsFailed,
                TotalProcessingTime = _statistics.TotalProcessingTime,
                OptimalBatchSize = _statistics.OptimalBatchSize,
                LastUpdated = _statistics.LastUpdated
            };
        }
    }

    public void ResetStatistics()
    {
        lock (_statsLock)
        {
            _statistics = new BatchProcessingStatistics();
        }
        
        _logger.LogInformation("Batch processing statistics reset");
    }

    private async Task ProcessItemWithSemaphoreAsync<T, TResult>(
        T document,
        Func<T, CancellationToken, Task<TResult>> processor,
        SemaphoreSlim semaphore,
        BatchProcessingOptions options,
        int index,
        ConcurrentBag<TResult> results,
        ConcurrentBag<BatchProcessingError> errors,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        
        try
        {
            var result = await ProcessItemWithRetryAsync(document, processor, options, index, cancellationToken);
            results.Add(result);
        }
        catch (Exception ex)
        {
            errors.Add(new BatchProcessingError
            {
                ItemIndex = index,
                ItemId = $"item-{index}",
                Exception = ex,
                ErrorMessage = ex.Message
            });
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<TResult> ProcessItemWithRetryAsync<T, TResult>(
        T document,
        Func<T, CancellationToken, Task<TResult>> processor,
        BatchProcessingOptions options,
        int index,
        CancellationToken cancellationToken)
    {
        var attempts = 0;
        Exception? lastException = null;

        while (attempts <= options.MaxRetryAttempts)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(options.ProcessingTimeout);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                
                return await processor(document, combinedCts.Token);
            }
            catch (Exception ex) when (options.EnableRetry && attempts < options.MaxRetryAttempts)
            {
                lastException = ex;
                attempts++;
                
                _logger.LogWarning(ex, "Processing item {Index} failed on attempt {Attempt}, retrying in {Delay}ms", 
                    index, attempts, options.RetryDelay.TotalMilliseconds);
                
                await Task.Delay(options.RetryDelay, cancellationToken);
            }
        }

        throw lastException ?? new InvalidOperationException($"Processing failed for item {index}");
    }

    private void UpdateStatistics<TResult>(BatchProcessingResult<TResult> result, int batchSize)
    {
        lock (_statsLock)
        {
            _statistics.TotalBatchesProcessed++;
            _statistics.TotalItemsProcessed += result.TotalProcessed;
            _statistics.TotalItemsFailed += result.Failed;
            _statistics.TotalProcessingTime += result.TotalDuration;
            _statistics.OptimalBatchSize = batchSize;
            _statistics.LastUpdated = DateTime.UtcNow;
        }
    }
}