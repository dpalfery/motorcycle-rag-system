using MotorcycleRAG.Domain.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Defines the contract for search agents
/// </summary>
public interface ISearchAgent
{
    /// <summary>
    /// The type of search agent
    /// </summary>
    SearchAgentType AgentType { get; }

    /// <summary>
    /// Performs search operation
    /// </summary>
    Task<SearchResult[]> SearchAsync(string query, SearchParameters options);
}
