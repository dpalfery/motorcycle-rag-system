using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for query planning agents
/// </summary>
public interface IQueryPlannerAgent : ISearchAgent
{
    /// <summary>
    /// Generates a query plan for the given query
    /// </summary>
    Task<QueryPlan> GeneratePlanAsync(string query, SearchParameters options);
}
