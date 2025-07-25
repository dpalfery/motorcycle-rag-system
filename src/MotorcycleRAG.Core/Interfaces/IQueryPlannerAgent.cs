using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Interface for query planner agent
/// </summary>
public interface IQueryPlannerAgent : ISearchAgent
{
    /// <summary>
    /// Generate a query plan for the given query
    /// </summary>
    Task<QueryPlan> GeneratePlanAsync(string query, SearchOptions options);
}
