namespace MotorcycleRAG.Contracts.Models.DTOs.Optimization;

/// <summary>
/// Settings for connection pool configuration.
/// </summary>
public class ConnectionPoolSettings {
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
