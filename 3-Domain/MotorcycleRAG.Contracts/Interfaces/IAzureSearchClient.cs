using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for Azure Search client operations
/// </summary>
public interface IAzureSearchClient
{
    /// <summary>
    /// Performs vector search
    /// </summary>
    Task<SearchResult[]> VectorSearchAsync(string query, SearchParameters options);

    /// <summary>
    /// Performs hybrid search (vector + keyword)
    /// </summary>
    Task<SearchResult[]> HybridSearchAsync(string query, SearchParameters options);

    /// <summary>
    /// Performs search (generic method)
    /// </summary>
    Task<SearchResult[]> SearchAsync(string query, SearchParameters options);

    /// <summary>
    /// Indexes documents
    /// </summary>
    Task IndexDocumentsAsync(IEnumerable<MotorcycleDocument> documents);

    /// <summary>
    /// Deletes documents from index
    /// </summary>
    Task DeleteDocumentsAsync(IEnumerable<string> documentIds);
}
