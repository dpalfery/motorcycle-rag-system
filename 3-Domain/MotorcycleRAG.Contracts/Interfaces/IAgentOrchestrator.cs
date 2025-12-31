using MotorcycleRAG.Domain.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Orchestrates execution of multiple search agents and generates a synthesized response.
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>
    /// Execute agents sequentially with coordination context.
    /// </summary>
    Task<SearchResult[]> ExecuteSequentialSearchAsync(string query, SearchContext context);

    /// <summary>
    /// Generate a natural language answer from collected search results.
    /// </summary>
    Task<string> GenerateResponseAsync(SearchResult[] results, string originalQuery);

    /// <summary>
    /// High-level entry point: perform orchestrated search with simple options.
    /// </summary>
    Task<SearchResult[]> OrchestrateSearchAsync(string query, SearchParameters options);

    /// <summary>
    /// Exposes the registered search agents.
    /// </summary>
    IEnumerable<ISearchAgent> GetAvailableAgents();
}
