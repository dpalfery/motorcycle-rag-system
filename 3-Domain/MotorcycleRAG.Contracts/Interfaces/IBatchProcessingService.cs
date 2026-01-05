using MotorcycleRAG.Contracts.Models.DTOs.Optimization;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Delegate for batch processing functions to avoid complex nested generic types.
/// </summary>
public delegate Task<IEnumerable<TResult>> BatchProcessor<T, TResult>(IEnumerable<T> documents, CancellationToken ct);

/// <summary>
/// Interface for optimized batch processing of data ingestion operations.
/// </summary>
public interface IBatchProcessingService {
    /// <summary>
    /// Processes documents in optimized batches.
    /// </summary>
    /// <typeparam name="T">Type of document to process</typeparam>
    /// <param name="documents">Documents to process</param>
    /// <param name="processor">Processing function</param>
    /// <param name="batchSize">Optimal batch size</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processing results</returns>
    Task<BatchProcessingResult<TResult>> ProcessBatchAsync<T, TResult>(
        IEnumerable<T> documents,
        BatchProcessor<T, TResult> processor,
        int batchSize = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes documents with parallel execution and load balancing.
    /// </summary>
    /// <typeparam name="T">Type of document to process</typeparam>
    /// <param name="documents">Documents to process</param>
    /// <param name="processor">Processing function</param>
    /// <param name="options">Processing options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processing results</returns>
    Task<BatchProcessingResult<TResult>> ProcessParallelBatchAsync<T, TResult>(
        IEnumerable<T> documents,
        Func<T, CancellationToken, Task<TResult>> processor,
        BatchProcessingOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimizes batch size based on document characteristics and system resources.
    /// </summary>
    /// <param name="documentCount">Number of documents</param>
    /// <param name="averageDocumentSize">Average document size in bytes</param>
    /// <param name="availableMemory">Available memory in bytes</param>
    /// <returns>Optimal batch size</returns>
    int OptimizeBatchSize(int documentCount, long averageDocumentSize, long availableMemory);

    /// <summary>
    /// Gets batch processing statistics for monitoring and optimization.
    /// </summary>
    /// <returns>Processing statistics</returns>
    BatchProcessingStatistics GetStatistics();

    /// <summary>
    /// Resets processing statistics.
    /// </summary>
    void ResetStatistics();
}
