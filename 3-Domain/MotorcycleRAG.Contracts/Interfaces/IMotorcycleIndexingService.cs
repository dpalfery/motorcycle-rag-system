using MotorcycleRAG.Contracts.Models.DTOs;


using MotorcycleRAG.Contracts.Models.DTOs.Search;
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for motorcycle indexing service operations
/// </summary>
public interface IMotorcycleIndexingService {
    /// <summary>
    /// Indexes motorcycle documents
    /// </summary>
    Task<BatchIndexingResult> IndexDocumentsAsync(IEnumerable<MotorcycleDocumentDto> documents);

    /// <summary>
    /// Gets indexing statistics
    /// </summary>
    Task<IndexingStatistics> GetIndexingStatisticsAsync();

    /// <summary>
    /// Rebuilds the search index
    /// </summary>
    Task<IndexCreationResult> RebuildIndexAsync();
}
