using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Interface for specialized search agents
/// </summary>
public interface ISearchAgent
{
    /// <summary>
    /// Execute search using this agent's specialized capabilities
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="options">Search options and parameters</param>
    /// <returns>Array of search results</returns>
    Task<SearchResult[]> SearchAsync(string query, SearchOptions options);

    /// <summary>
    /// The type of search agent
    /// </summary>
    SearchAgentType AgentType { get; }
}