using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for motorcycle indexing service operations
/// </summary>
public interface IMotorcycleIndexingService
{
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
