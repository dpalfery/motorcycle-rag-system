namespace MotorcycleRAG.Domain.DTOs.Optimization;

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
