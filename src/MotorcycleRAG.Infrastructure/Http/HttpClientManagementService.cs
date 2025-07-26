using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Models;
using System.Collections.Concurrent;
using System.Net;

namespace MotorcycleRAG.Infrastructure.Http;

/// <summary>
/// Configuration for HTTP client management
/// </summary>
public class HttpClientConfiguration
{
    /// <summary>
    /// Maximum number of connections per endpoint
    /// </summary>
    public int MaxConnectionsPerEndpoint { get; set; } = 10;

    /// <summary>
    /// Connection timeout in seconds
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 100;

    /// <summary>
    /// Connection pool timeout in seconds
    /// </summary>
    public int PooledConnectionLifetimeSeconds { get; set; } = 300;

    /// <summary>
    /// Enable connection pooling
    /// </summary>
    public bool EnableConnectionPooling { get; set; } = true;

    /// <summary>
    /// Enable HTTP/2 support
    /// </summary>
    public bool EnableHttp2 { get; set; } = true;

    /// <summary>
    /// Maximum number of HTTP clients to maintain
    /// </summary>
    public int MaxHttpClients { get; set; } = 50;
}

/// <summary>
/// Service for managing HTTP client connections and pooling
/// </summary>
public class HttpClientManagementService : IDisposable
{
    private readonly ILogger<HttpClientManagementService> _logger;
    private readonly HttpClientConfiguration _configuration;
    private readonly ConcurrentDictionary<string, HttpClient> _httpClients;
    private readonly ConcurrentDictionary<string, HttpClientStatistics> _clientStatistics;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public HttpClientManagementService(
        ILogger<HttpClientManagementService> logger,
        IOptions<HttpClientConfiguration> configuration)
    {
        _logger = logger;
        _configuration = configuration.Value;
        _httpClients = new ConcurrentDictionary<string, HttpClient>();
        _clientStatistics = new ConcurrentDictionary<string, HttpClientStatistics>();

        // Setup cleanup timer to run every 5 minutes
        _cleanupTimer = new Timer(CleanupIdleClients, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        _logger.LogInformation("HttpClientManagementService initialized with connection pooling: {EnablePooling}", 
            _configuration.EnableConnectionPooling);
    }

    /// <summary>
    /// Get or create an HTTP client for a specific endpoint
    /// </summary>
    /// <param name="clientName">Name/identifier for the client</param>
    /// <param name="baseAddress">Base address for the client</param>
    /// <returns>Configured HTTP client</returns>
    public HttpClient GetOrCreateClient(string clientName, string? baseAddress = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HttpClientManagementService));

        return _httpClients.GetOrAdd(clientName, _ => CreateHttpClient(clientName, baseAddress));
    }

    /// <summary>
    /// Get statistics for all HTTP clients
    /// </summary>
    /// <returns>Dictionary of client statistics</returns>
    public Dictionary<string, HttpClientStatistics> GetClientStatistics()
    {
        return _clientStatistics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Record a request for statistics tracking
    /// </summary>
    /// <param name="clientName">Name of the client</param>
    /// <param name="success">Whether the request was successful</param>
    /// <param name="responseTime">Response time in milliseconds</param>
    public void RecordRequest(string clientName, bool success, double responseTime)
    {
        var stats = _clientStatistics.GetOrAdd(clientName, _ => new HttpClientStatistics
        {
            ClientName = clientName,
            CreatedAt = DateTime.UtcNow
        });

        lock (stats)
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

            stats.AverageResponseTimeMs = (stats.AverageResponseTimeMs * (stats.TotalRequests - 1) + responseTime) / stats.TotalRequests;
            stats.LastRequestAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Create a new HTTP client with optimized settings
    /// </summary>
    /// <param name="clientName">Name for the client</param>
    /// <param name="baseAddress">Base address for the client</param>
    /// <returns>Configured HTTP client</returns>
    private HttpClient CreateHttpClient(string clientName, string? baseAddress = null)
    {
        try
        {
            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = _configuration.MaxConnectionsPerEndpoint,
                ConnectTimeout = TimeSpan.FromSeconds(_configuration.ConnectionTimeoutSeconds),
                PooledConnectionLifetime = TimeSpan.FromSeconds(_configuration.PooledConnectionLifetimeSeconds),
                UseCookies = false, // Disable cookies for better performance
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3
            };

            // Configure HTTP version support
            if (_configuration.EnableHttp2)
            {
                handler.SslOptions.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
            }

            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(_configuration.RequestTimeoutSeconds)
            };

            if (!string.IsNullOrEmpty(baseAddress))
            {
                httpClient.BaseAddress = new Uri(baseAddress);
            }

            // Set default headers for better performance
            httpClient.DefaultRequestHeaders.Add("User-Agent", "MotorcycleRAG-System/1.0");
            httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
            httpClient.DefaultRequestHeaders.ConnectionClose = false; // Keep connections alive

            // Initialize statistics
            _clientStatistics.TryAdd(clientName, new HttpClientStatistics
            {
                ClientName = clientName,
                CreatedAt = DateTime.UtcNow
            });

            _logger.LogDebug("Created HTTP client '{ClientName}' with base address '{BaseAddress}'", 
                clientName, baseAddress ?? "none");

            return httpClient;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating HTTP client '{ClientName}'", clientName);
            throw;
        }
    }

    /// <summary>
    /// Cleanup idle HTTP clients
    /// </summary>
    /// <param name="state">Timer state (unused)</param>
    private void CleanupIdleClients(object? state)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-30); // Remove clients idle for 30+ minutes
            var clientsToRemove = new List<string>();

            foreach (var kvp in _clientStatistics)
            {
                if (kvp.Value.LastRequestAt < cutoffTime)
                {
                    clientsToRemove.Add(kvp.Key);
                }
            }

            foreach (var clientName in clientsToRemove)
            {
                if (_httpClients.TryRemove(clientName, out var client))
                {
                    client.Dispose();
                    _clientStatistics.TryRemove(clientName, out _);
                    
                    _logger.LogDebug("Cleaned up idle HTTP client '{ClientName}'", clientName);
                }
            }

            if (clientsToRemove.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} idle HTTP clients", clientsToRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during HTTP client cleanup");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _cleanupTimer?.Dispose();

        foreach (var client in _httpClients.Values)
        {
            client.Dispose();
        }

        _httpClients.Clear();
        _clientStatistics.Clear();

        _disposed = true;
        _logger.LogInformation("HttpClientManagementService disposed");
    }
}

/// <summary>
/// Statistics for HTTP client usage
/// </summary>
public class HttpClientStatistics
{
    /// <summary>
    /// Name of the HTTP client
    /// </summary>
    public string ClientName { get; set; } = string.Empty;

    /// <summary>
    /// When the client was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last request timestamp
    /// </summary>
    public DateTime LastRequestAt { get; set; }

    /// <summary>
    /// Total number of requests made
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// Number of successful requests
    /// </summary>
    public long SuccessfulRequests { get; set; }

    /// <summary>
    /// Number of failed requests
    /// </summary>
    public long FailedRequests { get; set; }

    /// <summary>
    /// Success rate (0.0 to 1.0)
    /// </summary>
    public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 0.0;

    /// <summary>
    /// Average response time in milliseconds
    /// </summary>
    public double AverageResponseTimeMs { get; set; }
}