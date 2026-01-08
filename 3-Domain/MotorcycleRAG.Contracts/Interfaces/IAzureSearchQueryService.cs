using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for Azure AI Search query operations
/// </summary>
public interface IAzureSearchQueryService
{
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
}