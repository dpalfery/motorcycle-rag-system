using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Main service interface for the Motorcycle RAG system
/// </summary>
public interface IMotorcycleRAGService
{
    /// <summary>
    /// Process a motorcycle-related query and return comprehensive results
    /// </summary>
    /// <param name="request">The query request containing user query and preferences</param>
    /// <returns>Unified response combining information from all relevant sources</returns>
    Task<MotorcycleQueryResponse> QueryAsync(MotorcycleQueryRequest request);

    /// <summary>
    /// Get system health status
    /// </summary>
    /// <returns>Health check result indicating system status</returns>
    Task<HealthCheckResult> GetHealthAsync();
}