using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Main service interface for motorcycle RAG operations
/// </summary>
public interface IMotorcycleRagService {
    /// <summary>
    /// Processes a motorcycle-related natural language query and returns an AI-generated answer.
    /// </summary>
    Task<MotorcycleQueryResponse> QueryAsync(MotorcycleQueryRequest request);

    /// <summary>
    /// Searches for motorcycle information (alias for QueryAsync)
    /// </summary>
    Task<MotorcycleQueryResponse> SearchAsync(MotorcycleQueryRequest request);

    /// <summary>
    /// Gets service health status
    /// </summary>
    Task<HealthCheckResult> GetHealthAsync();
}
