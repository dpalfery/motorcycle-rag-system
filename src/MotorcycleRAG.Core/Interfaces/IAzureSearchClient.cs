using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Interface for Azure AI Search client with connection management
/// </summary>
public interface IAzureSearchClient
{
    /// <summary>
    /// Search documents with hybrid capabilities
    /// </summary>
    Task<SearchResult[]> SearchAsync(
        string searchText,
        int maxResults = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Index documents in batch
    /// </summary>
    Task<bool> IndexDocumentsAsync<T>(
        T[] documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create or update search index
    /// </summary>
    Task<bool> CreateOrUpdateIndexAsync(
        string indexName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the search service is healthy
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}