using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Infrastructure.Http;
using Xunit;

namespace MotorcycleRAG.UnitTests.Http;

public class HttpClientManagementServiceTests : IDisposable
{
    private readonly Mock<ILogger<HttpClientManagementService>> _mockLogger;
    private readonly HttpClientManagementService _httpClientManagementService;
    private readonly HttpClientConfiguration _config;

    public HttpClientManagementServiceTests()
    {
        _mockLogger = new Mock<ILogger<HttpClientManagementService>>();
        _config = new HttpClientConfiguration
        {
            MaxConnectionsPerEndpoint = 5,
            ConnectionTimeoutSeconds = 10,
            RequestTimeoutSeconds = 30,
            PooledConnectionLifetimeSeconds = 120,
            EnableConnectionPooling = true,
            EnableHttp2 = true,
            MaxHttpClients = 20
        };

        var options = Options.Create(_config);
        _httpClientManagementService = new HttpClientManagementService(_mockLogger.Object, options);
    }

    [Fact]
    public void GetOrCreateClient_WithNewClientName_CreatesNewClient()
    {
        // Arrange
        var clientName = "test-client";
        var baseAddress = "https://api.example.com";

        // Act
        var client = _httpClientManagementService.GetOrCreateClient(clientName, baseAddress);

        // Assert
        Assert.NotNull(client);
        Assert.Equal(baseAddress, client.BaseAddress?.ToString().TrimEnd('/'));
        Assert.Equal(TimeSpan.FromSeconds(_config.RequestTimeoutSeconds), client.Timeout);
    }

    [Fact]
    public void GetOrCreateClient_WithSameClientName_ReturnsSameInstance()
    {
        // Arrange
        var clientName = "test-client";

        // Act
        var client1 = _httpClientManagementService.GetOrCreateClient(clientName);
        var client2 = _httpClientManagementService.GetOrCreateClient(clientName);

        // Assert
        Assert.Same(client1, client2);
    }

    [Fact]
    public void GetOrCreateClient_WithoutBaseAddress_CreatesClientWithoutBaseAddress()
    {
        // Arrange
        var clientName = "test-client-no-base";

        // Act
        var client = _httpClientManagementService.GetOrCreateClient(clientName);

        // Assert
        Assert.NotNull(client);
        Assert.Null(client.BaseAddress);
    }

    [Fact]
    public void GetOrCreateClient_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        _httpClientManagementService.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _httpClientManagementService.GetOrCreateClient("test");
        });
    }

    [Fact]
    public void RecordRequest_WithValidData_UpdatesStatistics()
    {
        // Arrange
        var clientName = "test-client";
        _httpClientManagementService.GetOrCreateClient(clientName); // Create client first

        // Act
        _httpClientManagementService.RecordRequest(clientName, true, 150.5);
        _httpClientManagementService.RecordRequest(clientName, false, 300.0);
        _httpClientManagementService.RecordRequest(clientName, true, 75.2);

        var statistics = _httpClientManagementService.GetClientStatistics();

        // Assert
        Assert.True(statistics.ContainsKey(clientName));
        var stats = statistics[clientName];
        
        Assert.Equal(3, stats.TotalRequests);
        Assert.Equal(2, stats.SuccessfulRequests);
        Assert.Equal(1, stats.FailedRequests);
        Assert.Equal(2.0 / 3.0, stats.SuccessRate, precision: 2);
        Assert.True(stats.AverageResponseTimeMs > 0);
    }

    [Fact]
    public void GetClientStatistics_WithNoClients_ReturnsEmptyDictionary()
    {
        // Act
        var statistics = _httpClientManagementService.GetClientStatistics();

        // Assert
        Assert.NotNull(statistics);
        Assert.Empty(statistics);
    }

    [Fact]
    public void GetClientStatistics_WithMultipleClients_ReturnsAllStatistics()
    {
        // Arrange
        var client1Name = "client1";
        var client2Name = "client2";

        _httpClientManagementService.GetOrCreateClient(client1Name);
        _httpClientManagementService.GetOrCreateClient(client2Name);

        _httpClientManagementService.RecordRequest(client1Name, true, 100);
        _httpClientManagementService.RecordRequest(client2Name, false, 200);

        // Act
        var statistics = _httpClientManagementService.GetClientStatistics();

        // Assert
        Assert.Equal(2, statistics.Count);
        Assert.True(statistics.ContainsKey(client1Name));
        Assert.True(statistics.ContainsKey(client2Name));

        Assert.Equal(1, statistics[client1Name].TotalRequests);
        Assert.Equal(1, statistics[client1Name].SuccessfulRequests);
        Assert.Equal(1.0, statistics[client1Name].SuccessRate);

        Assert.Equal(1, statistics[client2Name].TotalRequests);
        Assert.Equal(0, statistics[client2Name].SuccessfulRequests);
        Assert.Equal(0.0, statistics[client2Name].SuccessRate);
    }

    [Fact]
    public void RecordRequest_WithNewClientName_CreatesStatisticsEntry()
    {
        // Arrange
        var clientName = "new-client";

        // Act
        _httpClientManagementService.RecordRequest(clientName, true, 50.0);
        var statistics = _httpClientManagementService.GetClientStatistics();

        // Assert
        Assert.True(statistics.ContainsKey(clientName));
        var stats = statistics[clientName];
        Assert.Equal(1, stats.TotalRequests);
        Assert.Equal(1, stats.SuccessfulRequests);
        Assert.Equal(50.0, stats.AverageResponseTimeMs);
    }

    [Fact]
    public void HttpClientConfiguration_DefaultValues_AreSet()
    {
        // Act & Assert
        var defaultConfig = new HttpClientConfiguration();
        
        Assert.Equal(10, defaultConfig.MaxConnectionsPerEndpoint);
        Assert.Equal(30, defaultConfig.ConnectionTimeoutSeconds);
        Assert.Equal(100, defaultConfig.RequestTimeoutSeconds);
        Assert.Equal(300, defaultConfig.PooledConnectionLifetimeSeconds);
        Assert.True(defaultConfig.EnableConnectionPooling);
        Assert.True(defaultConfig.EnableHttp2);
        Assert.Equal(50, defaultConfig.MaxHttpClients);
    }

    [Fact]
    public void HttpClientStatistics_SuccessRate_CalculatesCorrectly()
    {
        // Arrange
        var stats = new HttpClientStatistics
        {
            TotalRequests = 10,
            SuccessfulRequests = 7,
            FailedRequests = 3
        };

        // Act & Assert
        Assert.Equal(0.7, stats.SuccessRate, precision: 1);
    }

    [Fact]
    public void HttpClientStatistics_SuccessRateWithZeroRequests_ReturnsZero()
    {
        // Arrange
        var stats = new HttpClientStatistics
        {
            TotalRequests = 0,
            SuccessfulRequests = 0,
            FailedRequests = 0
        };

        // Act & Assert
        Assert.Equal(0.0, stats.SuccessRate);
    }

    [Fact]
    public void GetOrCreateClient_WithValidBaseAddress_SetsCorrectBaseAddress()
    {
        // Arrange
        var clientName = "test-client";
        var baseAddress = "https://api.motorcycles.com/v1/";

        // Act
        var client = _httpClientManagementService.GetOrCreateClient(clientName, baseAddress);

        // Assert
        Assert.NotNull(client.BaseAddress);
        Assert.Equal(baseAddress, client.BaseAddress.ToString());
    }

    [Fact]
    public void GetOrCreateClient_SetsDefaultHeaders()
    {
        // Arrange
        var clientName = "test-client";

        // Act
        var client = _httpClientManagementService.GetOrCreateClient(clientName);

        // Assert
        Assert.True(client.DefaultRequestHeaders.Contains("User-Agent"));
        Assert.True(client.DefaultRequestHeaders.Contains("Accept-Encoding"));
        Assert.False(client.DefaultRequestHeaders.ConnectionClose ?? false);
    }

    [Fact]
    public void RecordRequest_UpdatesAverageResponseTimeCorrectly()
    {
        // Arrange
        var clientName = "test-client";
        _httpClientManagementService.GetOrCreateClient(clientName);

        // Act
        _httpClientManagementService.RecordRequest(clientName, true, 100.0);
        _httpClientManagementService.RecordRequest(clientName, true, 200.0);
        _httpClientManagementService.RecordRequest(clientName, true, 300.0);

        var statistics = _httpClientManagementService.GetClientStatistics();

        // Assert
        var stats = statistics[clientName];
        Assert.Equal(200.0, stats.AverageResponseTimeMs, precision: 1);
    }

    [Fact]
    public void RecordRequest_UpdatesLastRequestTime()
    {
        // Arrange
        var clientName = "test-client";
        var beforeRequest = DateTime.UtcNow;
        _httpClientManagementService.GetOrCreateClient(clientName);

        // Act
        _httpClientManagementService.RecordRequest(clientName, true, 100.0);
        var afterRequest = DateTime.UtcNow;

        var statistics = _httpClientManagementService.GetClientStatistics();

        // Assert
        var stats = statistics[clientName];
        Assert.True(stats.LastRequestAt >= beforeRequest);
        Assert.True(stats.LastRequestAt <= afterRequest);
    }

    public void Dispose()
    {
        _httpClientManagementService?.Dispose();
    }
}