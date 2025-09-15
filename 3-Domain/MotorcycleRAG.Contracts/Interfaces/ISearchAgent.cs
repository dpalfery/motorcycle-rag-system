using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Defines the contract for search agents
/// </summary>
public interface ISearchAgent
{
    /// <summary>
    /// The type of search agent
    /// </summary>
    Domain.Models.SearchAgentType AgentType { get; }

    /// <summary>
    /// Performs search operation
    /// </summary>
    Task<Domain.Models.SearchResult[]> SearchAsync(string query, Domain.Models.SearchOptions options);
}
