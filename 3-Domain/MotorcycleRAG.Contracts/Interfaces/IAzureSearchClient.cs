using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

using MotorcycleRAG.Contracts.Models.DTOs.Search;
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for Azure Search client operations
/// </summary>
public interface IAzureSearchClient {
    /// <summary>
    /// Performs vector search
    /// </summary>
    Task<SearchResult[]> VectorSearchAsync(string query, SearchOptions options);

    /// <summary>
    /// Performs hybrid search (vector + keyword)
    /// </summary>
    Task<SearchResult[]> HybridSearchAsync(string query, SearchOptions options);

    /// <summary>
    /// Performs search (generic method)
    /// </summary>
    Task<SearchResult[]> SearchAsync(string query, SearchOptions options);

    /// <summary>
    /// Indexes documents
    /// </summary>
    Task IndexDocumentsAsync(IEnumerable<MotorcycleDocumentDto> documents);

    /// <summary>
    /// Deletes documents from index
    /// </summary>
    Task DeleteDocumentsAsync(IEnumerable<string> documentIds);
}
