using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Interface for motorcycle-specific indexing service with hybrid vector/keyword capabilities
/// </summary>
public interface IMotorcycleIndexingService : IDisposable
{
    /// <summary>
    /// Create or update search indexes with hybrid vector/keyword capabilities
    /// </summary>
    Task<IndexCreationResult> CreateSearchIndexesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Index CSV processed data with batch processing (100-1000 documents per batch)
    /// </summary>
    Task<IndexingResult> IndexCSVDataAsync(ProcessedData processedData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Index PDF processed data with batch processing
    /// </summary>
    Task<IndexingResult> IndexPDFDataAsync(ProcessedData processedData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get indexing statistics and health information
    /// </summary>
    Task<IndexingStatistics> GetIndexingStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete and recreate all indexes (for maintenance/reset scenarios)
    /// </summary>
    Task<bool> ResetIndexesAsync(CancellationToken cancellationToken = default);
}