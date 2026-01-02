using MotorcycleRAG.Domain.DTOs.Optimization;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for managing connection pools and HTTP client optimization.
/// </summary>
public interface IConnectionPoolService
{
    /// <summary>
    /// Gets an optimized HTTP client for the specified service.
    /// </summary>
    /// <param name="serviceName">Name of the service</param>
    /// <returns>Configured HTTP client</returns>
    HttpClient GetHttpClient(string serviceName);

    /// <summary>
    /// Gets connection pool statistics for monitoring.
    /// </summary>
    /// <param name="serviceName">Name of the service</param>
    /// <returns>Connection pool statistics</returns>
    ConnectionPoolStatistics GetStatistics(string serviceName);

    /// <summary>
    /// Gets statistics for all connection pools.
    /// </summary>
    /// <returns>Dictionary of connection pool statistics by service name</returns>
    Dictionary<string, ConnectionPoolStatistics> GetAllStatistics();

    /// <summary>
    /// Configures connection pool settings for a service.
    /// </summary>
    /// <param name="serviceName">Name of the service</param>
    /// <param name="settings">Connection pool settings</param>
    void ConfigureConnectionPool(string serviceName, ConnectionPoolSettings settings);

    /// <summary>
    /// Performs health check on connection pools.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check results</returns>
    Task<Dictionary<string, bool>> HealthCheckAsync(CancellationToken cancellationToken = default);
}
