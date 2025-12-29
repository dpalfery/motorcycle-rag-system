using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using MotorcycleRAG.Contracts.Optimization;


namespace MotorcycleRAG.Application.Optimization;

/// <summary>
/// Implementation of connection pool service for optimized HTTP client management.
/// </summary>
public class ConnectionPoolService : IConnectionPoolService, IDisposable
{
    private readonly ILogger<ConnectionPoolService> _logger;
    private readonly ConcurrentDictionary<string, HttpClient> _httpClients = new();
    private readonly ConcurrentDictionary<string, ConnectionPoolSettings> _settings = new();
    private readonly ConcurrentDictionary<string, ConnectionPoolStatistics> _statistics = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public ConnectionPoolService(ILogger<ConnectionPoolService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Initialize cleanup timer to run every 5 minutes
        _cleanupTimer = new Timer(CleanupConnections, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        
        _logger.LogInformation("Connection pool service initialized");
    }

    public HttpClient GetHttpClient(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        return _httpClients.GetOrAdd(serviceName, CreateHttpClient);
    }

    public ConnectionPoolStatistics GetStatistics(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        return _statistics.GetOrAdd(serviceName, _ => new ConnectionPoolStatistics { ServiceName = serviceName });
    }

    public Dictionary<string, ConnectionPoolStatistics> GetAllStatistics()
    {
        return _statistics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public void ConfigureConnectionPool(string serviceName, ConnectionPoolSettings settings)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        _settings.AddOrUpdate(serviceName, settings, (_, _) => settings);
        
        // If client already exists, recreate it with new settings
        if (_httpClients.TryRemove(serviceName, out var existingClient))
        {
            existingClient.Dispose();
            _logger.LogInformation("Recreated HTTP client for service {ServiceName} with new settings", serviceName);
        }
    }

    public async Task<Dictionary<string, bool>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, bool>();
        var tasks = new List<Task>();

        foreach (var serviceName in _httpClients.Keys)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var client = GetHttpClient(serviceName);
                    var settings = _settings.GetValueOrDefault(serviceName, new ConnectionPoolSettings());
                    
                    using var cts = new CancellationTokenSource(settings.ConnectionTimeout);
                    using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
                    
                    // Simple connectivity test - this would be customized per service
                    var response = await client.GetAsync("/health", HttpCompletionOption.ResponseHeadersRead, combinedCts.Token);
                    
                    lock (results)
                    {
                        results[serviceName] = response.IsSuccessStatusCode;
                    }

                    UpdateStatistics(serviceName, true, TimeSpan.Zero);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Health check failed for service {ServiceName}", serviceName);
                    
                    lock (results)
                    {
                        results[serviceName] = false;
                    }

                    UpdateStatistics(serviceName, false, TimeSpan.Zero);
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        return results;
    }

    private HttpClient CreateHttpClient(string serviceName)
    {
        var settings = _settings.GetValueOrDefault(serviceName, new ConnectionPoolSettings());
        
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = settings.MaxConnectionsPerEndpoint,
            ConnectTimeout = settings.ConnectionTimeout,
            PooledConnectionIdleTimeout = settings.ConnectionIdleTimeout,
            PooledConnectionLifetime = settings.ConnectionLifetime,
            UseCookies = false, // Disable cookies for better performance
            AutomaticDecompression = settings.EnableCompression ? 
                (DecompressionMethods.GZip | DecompressionMethods.Deflate) : 
                DecompressionMethods.None
        };

        var client = new HttpClient(handler)
        {
            Timeout = settings.ConnectionTimeout
        };

        // Add default headers
        foreach (var header in settings.DefaultHeaders)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Add user agent
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MotorcycleRAG/1.0");

        // Initialize statistics
        _statistics.TryAdd(serviceName, new ConnectionPoolStatistics 
        { 
            ServiceName = serviceName,
            LastActivity = DateTime.UtcNow
        });

        _logger.LogInformation("Created HTTP client for service {ServiceName} with settings: MaxConnections={MaxConnections}, Timeout={Timeout}",
            serviceName, settings.MaxConnectionsPerEndpoint, settings.ConnectionTimeout);

        return client;
    }

    private void UpdateStatistics(string serviceName, bool success, TimeSpan responseTime)
    {
        if (_statistics.TryGetValue(serviceName, out var stats))
        {
            stats.TotalRequests++;
            if (success)
            {
                stats.SuccessfulRequests++;
            }
            else
            {
                stats.FailedRequests++;
            }
            
            // Update average response time (simple moving average)
            if (stats.TotalRequests == 1)
            {
                stats.AverageResponseTime = responseTime;
            }
            else
            {
                var totalMs = stats.AverageResponseTime.TotalMilliseconds * (stats.TotalRequests - 1) + responseTime.TotalMilliseconds;
                stats.AverageResponseTime = TimeSpan.FromMilliseconds(totalMs / stats.TotalRequests);
            }
            
            stats.LastActivity = DateTime.UtcNow;
            stats.IsHealthy = stats.SuccessRate > 0.95; // Consider healthy if >95% success rate
        }
    }

    private void CleanupConnections(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var clientsToRemove = new List<string>();

            foreach (var kvp in _statistics)
            {
                var serviceName = kvp.Key;
                var stats = kvp.Value;
                
                // Remove clients that haven't been used in the last hour
                if (now - stats.LastActivity > TimeSpan.FromHours(1))
                {
                    clientsToRemove.Add(serviceName);
                }
            }

            foreach (var serviceName in clientsToRemove)
            {
                if (_httpClients.TryRemove(serviceName, out var client))
                {
                    client.Dispose();
                    _statistics.TryRemove(serviceName, out _);
                    _logger.LogDebug("Cleaned up unused HTTP client for service {ServiceName}", serviceName);
                }
            }

            if (clientsToRemove.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} unused HTTP clients", clientsToRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during connection cleanup");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cleanupTimer?.Dispose();
            
            foreach (var client in _httpClients.Values)
            {
                client.Dispose();
            }
            
            _httpClients.Clear();
            _statistics.Clear();
            _settings.Clear();
            
            _disposed = true;
            _logger.LogInformation("Connection pool service disposed");
        }
    }
}