using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for motorcycle indexing service operations
/// </summary>
public interface IMotorcycleIndexingService
{
    /// <summary>
    /// Creates or updates search indexes with hybrid vector/keyword capabilities
    /// </summary>
    Task<IndexCreationResult> CreateSearchIndexesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Index CSV processed data with batch processing
    /// </summary>
    Task<IndexingResult> IndexCSVDataAsync(ProcessedData processedData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Index PDF processed data with batch processing
    /// </summary>
    Task<IndexingResult> IndexPDFDataAsync(ProcessedData processedData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexes motorcycle documents
    /// </summary>
    Task<BatchIndexingResult> IndexDocumentsAsync(IEnumerable<MotorcycleDocument> documents);

    /// <summary>
    /// Gets indexing statistics
    /// </summary>
    Task<IndexingStatistics> GetIndexingStatisticsAsync();

    /// <summary>
    /// Rebuilds the search index
    /// </summary>
    Task<IndexCreationResult> RebuildIndexAsync();
}
