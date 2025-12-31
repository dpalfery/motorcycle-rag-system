using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Domain.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Defines the contract for query planner agents
/// </summary>
public interface IQueryPlannerAgent : ISearchAgent
{
    /// <summary>
    /// Plans a search strategy for the given query
    /// </summary>
    Task<string> PlanQueryAsync(string userQuery);
}
