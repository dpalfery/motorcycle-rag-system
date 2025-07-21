using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Interface for multi-agent coordination using Semantic Kernel Agent Framework
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>
    /// Execute sequential search pattern across multiple agents
    /// </summary>
    /// <param name="query">The user query to process</param>
    /// <param name="context">Search context and preferences</param>
    /// <returns>Array of search results from coordinated agents</returns>
    Task<SearchResult[]> ExecuteSequentialSearchAsync(string query, SearchContext context);

    /// <summary>
    /// Generate unified response from multiple search results
    /// </summary>
    /// <param name="results">Search results from different agents</param>
    /// <param name="originalQuery">The original user query</param>
    /// <returns>Unified response combining all results</returns>
    Task<string> GenerateResponseAsync(SearchResult[] results, string originalQuery);
}