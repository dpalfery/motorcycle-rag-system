using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Interface for optimized batch processing of data ingestion
/// </summary>
public interface IBatchProcessingService
{
    /// <summary>
    /// Process documents in optimized batches
    /// </summary>
    /// <param name="documents">Documents to process</param>
    /// <param name="batchSize">Number of documents per batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processing results</returns>
    Task<BatchProcessingResult> ProcessDocumentBatchAsync(
        IEnumerable<ProcessingDocument> documents, 
        int batchSize = 100, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Process embeddings in optimized batches
    /// </summary>
    /// <param name="texts">Texts to generate embeddings for</param>
    /// <param name="batchSize">Number of texts per batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generated embeddings</returns>
    Task<float[][]> ProcessEmbeddingBatchAsync(
        IEnumerable<string> texts, 
        int batchSize = 100, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Index documents in optimized batches
    /// </summary>
    /// <param name="documents">Documents to index</param>
    /// <param name="batchSize">Number of documents per batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Indexing results</returns>
    Task<BatchProcessingIndexingResult> IndexDocumentBatchAsync(
        IEnumerable<MotorcycleDocument> documents, 
        int batchSize = 100, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get optimal batch size based on system resources and document size
    /// </summary>
    /// <param name="averageDocumentSize">Average document size in bytes</param>
    /// <param name="availableMemory">Available memory in bytes</param>
    /// <returns>Recommended batch size</returns>
    int GetOptimalBatchSize(long averageDocumentSize, long availableMemory);
}