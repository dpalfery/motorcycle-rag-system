using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MotorcycleRAG.Infrastructure.BatchProcessing;

/// <summary>
/// Implementation of optimized batch processing service for data ingestion
/// </summary>
public class BatchProcessingService : IBatchProcessingService
{
    private readonly ILogger<BatchProcessingService> _logger;
    private readonly BatchProcessingConfiguration _configuration;
    private readonly IAzureOpenAIClient _openAIClient;
    private readonly IMotorcycleIndexingService _indexingService;

    public BatchProcessingService(
        ILogger<BatchProcessingService> logger,
        IOptions<BatchProcessingConfiguration> configuration,
        IAzureOpenAIClient openAIClient,
        IMotorcycleIndexingService indexingService)
    {
        _logger = logger;
        _configuration = configuration.Value;
        _openAIClient = openAIClient;
        _indexingService = indexingService;
    }

    public async Task<BatchProcessingResult> ProcessDocumentBatchAsync(
        IEnumerable<ProcessingDocument> documents, 
        int batchSize = 100, 
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var documentList = documents.ToList();
        var totalDocuments = documentList.Count;
        var errors = new ConcurrentBag<ProcessingError>();
        var successCount = 0;
        var peakMemoryUsage = GC.GetTotalMemory(false);

        try
        {
            _logger.LogInformation("Starting batch processing of {Count} documents with batch size {BatchSize}", 
                totalDocuments, batchSize);

            // Adjust batch size based on configuration and memory constraints
            var optimalBatchSize = GetOptimalBatchSize(
                documentList.Count > 0 ? (long)documentList.Average(d => d.SizeBytes) : 1024,
                GC.GetTotalMemory(false));
            
            var actualBatchSize = Math.Min(batchSize, optimalBatchSize);
            actualBatchSize = Math.Min(actualBatchSize, _configuration.MaxBatchSize);

            _logger.LogDebug("Using optimal batch size: {OptimalBatchSize}", actualBatchSize);

            // Process documents in batches
            var batches = ChunkDocuments(documentList, actualBatchSize);
            var semaphore = new SemaphoreSlim(_configuration.ParallelProcessingThreads);
            var tasks = new List<Task>();

            foreach (var batch in batches)
            {
                tasks.Add(ProcessBatchAsync(batch, errors, semaphore, cancellationToken));
            }

            await Task.WhenAll(tasks);
            successCount = totalDocuments - errors.Count;

            // Update peak memory usage
            var currentMemory = GC.GetTotalMemory(false);
            if (currentMemory > peakMemoryUsage)
                peakMemoryUsage = currentMemory;

            stopwatch.Stop();

            var result = new BatchProcessingResult
            {
                TotalDocuments = totalDocuments,
                SuccessfullyProcessed = successCount,
                Failed = errors.Count,
                Errors = errors.ToList(),
                ProcessingTime = stopwatch.Elapsed,
                PeakMemoryUsageBytes = peakMemoryUsage
            };

            _logger.LogInformation("Batch processing completed. Success: {Success}/{Total}, Time: {Time}, Throughput: {Throughput:F2} docs/sec",
                successCount, totalDocuments, stopwatch.Elapsed, result.ThroughputPerSecond);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during batch processing");
            throw;
        }
    }

    public async Task<float[][]> ProcessEmbeddingBatchAsync(
        IEnumerable<string> texts, 
        int batchSize = 100, 
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        var totalTexts = textList.Count;
        var results = new List<float[]>();

        try
        {
            _logger.LogInformation("Generating embeddings for {Count} texts in batches of {BatchSize}", 
                totalTexts, batchSize);

            // Process embeddings in batches to avoid API rate limits
            var batches = ChunkTexts(textList, batchSize);
            
            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var embeddings = await _openAIClient.GetEmbeddingsAsync("text-embedding-3-large", batch.ToArray(), cancellationToken);
                    results.AddRange(embeddings);

                    _logger.LogDebug("Generated embeddings for batch of {Count} texts", batch.Count);

                    // Small delay to avoid overwhelming the API
                    if (batch != batches.Last())
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating embeddings for batch");
                    
                    // Add empty embeddings for failed batch to maintain index alignment
                    for (int i = 0; i < batch.Count; i++)
                    {
                        results.Add(new float[1536]); // Default embedding dimension for text-embedding-3-large
                    }
                }
            }

            _logger.LogInformation("Embedding generation completed for {Count} texts", results.Count);
            return results.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during embedding batch processing");
            throw;
        }
    }

    public async Task<BatchProcessingIndexingResult> IndexDocumentBatchAsync(
        IEnumerable<MotorcycleDocument> documents, 
        int batchSize = 100, 
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var documentList = documents.ToList();
        var totalDocuments = documentList.Count;
        var errors = new ConcurrentBag<ProcessingError>();
        var successCount = 0;

        try
        {
            _logger.LogInformation("Starting batch indexing of {Count} documents with batch size {BatchSize}", 
                totalDocuments, batchSize);

            // Process documents in batches
            var batches = ChunkMotorcycleDocuments(documentList, batchSize);
            
            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Convert to ProcessedData format for indexing
                    var processedData = new ProcessedData
                    {
                        Id = Guid.NewGuid().ToString(),
                        Documents = batch.ToList(),
                        ProcessedAt = DateTime.UtcNow
                    };
                    await _indexingService.IndexCSVDataAsync(processedData, cancellationToken);
                    successCount += batch.Count;

                    _logger.LogDebug("Successfully indexed batch of {Count} documents", batch.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error indexing batch of documents");
                    
                    foreach (var doc in batch)
                    {
                        errors.Add(new ProcessingError
                        {
                            DocumentId = doc.Id,
                            ErrorMessage = ex.Message,
                            ErrorCode = "INDEXING_FAILED"
                        });
                    }
                }
            }

            stopwatch.Stop();

            var result = new BatchProcessingIndexingResult
            {
                TotalDocuments = totalDocuments,
                SuccessfullyIndexed = successCount,
                Failed = errors.Count,
                Errors = errors.ToList(),
                IndexingTime = stopwatch.Elapsed,
                Statistics = new IndexStatistics
                {
                    TotalDocuments = successCount,
                    LastUpdated = DateTime.UtcNow
                }
            };

            _logger.LogInformation("Batch indexing completed. Success: {Success}/{Total}, Time: {Time}",
                successCount, totalDocuments, stopwatch.Elapsed);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during batch indexing");
            throw;
        }
    }

    public int GetOptimalBatchSize(long averageDocumentSize, long availableMemory)
    {
        try
        {
            // Calculate optimal batch size based on memory constraints
            var maxMemoryPerBatch = _configuration.MaxMemoryPerBatchMB * 1024 * 1024L;
            var memoryBasedBatchSize = (int)(maxMemoryPerBatch / Math.Max(averageDocumentSize, 1024));

            // Ensure it's within configured limits
            var optimalSize = Math.Min(memoryBasedBatchSize, _configuration.MaxBatchSize);
            optimalSize = Math.Max(optimalSize, 10); // Minimum batch size

            _logger.LogDebug("Calculated optimal batch size: {OptimalSize} (avg doc size: {AvgSize} bytes, available memory: {Memory} bytes)",
                optimalSize, averageDocumentSize, availableMemory);

            return optimalSize;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating optimal batch size, using default");
            return _configuration.DefaultBatchSize;
        }
    }

    private async Task ProcessBatchAsync(
        List<ProcessingDocument> batch, 
        ConcurrentBag<ProcessingError> errors, 
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        
        try
        {
            // Simulate document processing - in real implementation, this would
            // involve actual document processing logic
            foreach (var document in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Process individual document
                    await ProcessSingleDocumentAsync(document, cancellationToken);
                }
                catch (Exception ex)
                {
                    errors.Add(new ProcessingError
                    {
                        DocumentId = document.Id,
                        ErrorMessage = ex.Message,
                        ErrorCode = "PROCESSING_FAILED"
                    });
                }
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task ProcessSingleDocumentAsync(ProcessingDocument document, CancellationToken cancellationToken)
    {
        // Simulate processing time
        await Task.Delay(10, cancellationToken);
        
        // In real implementation, this would involve:
        // - Content extraction and validation
        // - Chunking strategies
        // - Embedding generation
        // - Metadata processing
    }

    private static List<List<ProcessingDocument>> ChunkDocuments(List<ProcessingDocument> documents, int batchSize)
    {
        var chunks = new List<List<ProcessingDocument>>();
        for (int i = 0; i < documents.Count; i += batchSize)
        {
            chunks.Add(documents.Skip(i).Take(batchSize).ToList());
        }
        return chunks;
    }

    private static List<List<string>> ChunkTexts(List<string> texts, int batchSize)
    {
        var chunks = new List<List<string>>();
        for (int i = 0; i < texts.Count; i += batchSize)
        {
            chunks.Add(texts.Skip(i).Take(batchSize).ToList());
        }
        return chunks;
    }

    private static List<List<MotorcycleDocument>> ChunkMotorcycleDocuments(List<MotorcycleDocument> documents, int batchSize)
    {
        var chunks = new List<List<MotorcycleDocument>>();
        for (int i = 0; i < documents.Count; i += batchSize)
        {
            chunks.Add(documents.Skip(i).Take(batchSize).ToList());
        }
        return chunks;
    }
}