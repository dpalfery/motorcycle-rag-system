namespace MotorcycleRAG.Application.Optimization;

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

/// <summary>
/// Settings for connection pool configuration.
/// </summary>
public class ConnectionPoolSettings
{
    public int MaxConnectionsPerEndpoint { get; set; } = 10;
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ConnectionIdleTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan ConnectionLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public bool EnableKeepAlive { get; set; } = true;
    public bool EnableCompression { get; set; } = true;
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public Dictionary<string, string> DefaultHeaders { get; set; } = new();
}

/// <summary>
/// Statistics for connection pool monitoring.
/// </summary>
public class ConnectionPoolStatistics
{
    public string ServiceName { get; set; } = string.Empty;
    public int ActiveConnections { get; set; }
    public int IdleConnections { get; set; }
    public int TotalConnectionsCreated { get; set; }
    public int TotalConnectionsDestroyed { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 0;
    public TimeSpan AverageResponseTime { get; set; }
    public DateTime LastActivity { get; set; }
    public bool IsHealthy { get; set; } = true;
}