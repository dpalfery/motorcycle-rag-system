using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services.Optimization;
using MotorcycleRAG.Contracts.Models.DTOs.Optimization;

namespace MotorcycleRAG.UnitTests.Services.Optimization;

public sealed class ConnectionPoolServiceTests
{
    [Fact]
    public void GetHttpClient_WithConfiguration_RecreatesTheClientAndAppliesHeaders()
    {
        // Arrange
        using var sut = CreateSut();
        var first = sut.GetHttpClient("search");
        var settings = new ConnectionPoolSettings
        {
            EnableCompression = false,
            ConnectionTimeout = TimeSpan.FromSeconds(5),
            DefaultHeaders = new Dictionary<string, string> { ["X-Environment"] = "test" },
        };

        // Act
        sut.ConfigureConnectionPool("search", settings);
        var recreated = sut.GetHttpClient("search");

        // Assert
        recreated.Should().NotBeSameAs(first);
        recreated.DefaultRequestHeaders.UserAgent.ToString().Should().Be("MotorcycleRAG/1.0");
        recreated.DefaultRequestHeaders.GetValues("X-Environment").Should().Equal("test");
        sut.GetStatistics("search").ServiceName.Should().Be("search");
    }

    [Fact]
    public void PublicMethods_WithInvalidArguments_ThrowActionableExceptions()
    {
        // Arrange
        using var sut = CreateSut();

        // Act
        var emptyClientName = () => sut.GetHttpClient(" ");
        var emptyStatisticsName = () => sut.GetStatistics(string.Empty);
        var emptyConfigurationName = () => sut.ConfigureConnectionPool("", new ConnectionPoolSettings());
        var nullSettings = () => sut.ConfigureConnectionPool("search", null!);

        // Assert
        emptyClientName.Should().Throw<ArgumentException>();
        emptyStatisticsName.Should().Throw<ArgumentException>();
        emptyConfigurationName.Should().Throw<ArgumentException>();
        nullSettings.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task HealthCheckAsync_WithInMemorySuccessAndFailureClients_RecordsBothOutcomes()
    {
        // Arrange
        using var sut = CreateSut();
        AddTestClient(sut, "healthy", HttpStatusCode.OK);
        sut.GetStatistics("healthy");
        sut.GetHttpClient("broken");

        // Act
        var results = await sut.HealthCheckAsync();
        var statistics = sut.GetAllStatistics();

        // Assert
        results.Should().Contain(new KeyValuePair<string, bool>("healthy", true));
        results.Should().Contain(new KeyValuePair<string, bool>("broken", false));
        statistics["healthy"].SuccessfulRequests.Should().Be(1);
        statistics["healthy"].IsHealthy.Should().BeTrue();
        statistics["broken"].FailedRequests.Should().Be(1);
        statistics["broken"].IsHealthy.Should().BeFalse();
    }

    [Fact]
    public void CleanupConnections_WithStaleClient_RemovesTheClientAndItsStatistics()
    {
        // Arrange
        using var sut = CreateSut();
        var first = sut.GetHttpClient("stale");
        sut.GetStatistics("stale").LastActivity = DateTime.UtcNow.AddHours(-2);

        // Act
        InvokeCleanup(sut);
        var recreated = sut.GetHttpClient("stale");

        // Assert
        sut.GetAllStatistics().Should().ContainKey("stale");
        recreated.Should().NotBeSameAs(first);
    }

    [Fact]
    public void Dispose_WithInitializedClients_ClearsStateAndIsIdempotent()
    {
        // Arrange
        var sut = CreateSut();
        sut.GetHttpClient("search");
        sut.GetStatistics("search");

        // Act
        sut.Dispose();
        sut.Dispose();

        // Assert
        sut.GetAllStatistics().Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ConnectionPoolService(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    private static ConnectionPoolService CreateSut() =>
        new(NullLogger<ConnectionPoolService>.Instance);

    private static void AddTestClient(ConnectionPoolService sut, string serviceName, HttpStatusCode statusCode)
    {
        var clients = (ConcurrentDictionary<string, Lazy<HttpClient>>)typeof(ConnectionPoolService)
            .GetField("_httpClients", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(sut)!;
        clients.TryAdd(serviceName, new Lazy<HttpClient>(() => new HttpClient(new StatusCodeHandler(statusCode))
        {
            BaseAddress = new Uri("https://unit-test.local"),
        }));
    }

    private static void InvokeCleanup(ConnectionPoolService sut) =>
        typeof(ConnectionPoolService)
            .GetMethod("CleanupConnections", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(sut, [null]);

    private sealed class StatusCodeHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
