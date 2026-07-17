using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services.Mcp;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Mcp;

public class McpToolManagerTests
{
    private readonly Mock<IToolConfigurationService> _configServiceMock;
    private readonly Mock<ILogger<McpToolManager>> _loggerMock;

    public McpToolManagerTests()
    {
        _configServiceMock = new Mock<IToolConfigurationService>();
        _loggerMock = new Mock<ILogger<McpToolManager>>();
    }

    [Fact]
    public void Constructor_NullDependencies_Throws()
    {
        var logger = NullLogger<McpToolManager>.Instance;
        Assert.Throws<ArgumentNullException>(() => new McpToolManager(null!, logger));
        Assert.Throws<ArgumentNullException>(() => new McpToolManager(_configServiceMock.Object, null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidRefreshInterval_ThrowsArgumentOutOfRangeException(int seconds)
    {
        var interval = TimeSpan.FromSeconds(seconds);
        var logger = NullLogger<McpToolManager>.Instance;

        Assert.Throws<ArgumentOutOfRangeException>(() => 
            new McpToolManager(_configServiceMock.Object, interval, logger));
    }

    [Fact]
    public async Task InitializeAsync_Success_LogsInformation()
    {
        var configs = new[] { CreateConfig("1") };
        _configServiceMock.Setup(x => x.GetEnabledToolsAsync()).ReturnsAsync(configs);

        var sut = new McpToolManager(_configServiceMock.Object, _loggerMock.Object);
        await sut.InitializeAsync();

        _configServiceMock.Verify(x => x.GetEnabledToolsAsync(), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("MCP tools initialized successfully")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_Exception_LogsWarning()
    {
        var exception = new Exception("DB failed");
        _configServiceMock.Setup(x => x.GetEnabledToolsAsync()).ThrowsAsync(exception);

        var sut = new McpToolManager(_configServiceMock.Object, _loggerMock.Object);
        await sut.InitializeAsync();

        _configServiceMock.Verify(x => x.GetEnabledToolsAsync(), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to refresh MCP tools")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetEnabledToolsAsync_ReturnsCachedValue_WhenWithinRefreshInterval()
    {
        var configs1 = new[] { CreateConfig("1") };
        var configs2 = new[] { CreateConfig("2") };
        
        _configServiceMock.SetupSequence(x => x.GetEnabledToolsAsync())
            .ReturnsAsync(configs1)
            .ReturnsAsync(configs2);

        var sut = new McpToolManager(_configServiceMock.Object, TimeSpan.FromSeconds(30), NullLogger<McpToolManager>.Instance);

        // First call - should hit the provider
        var result1 = await sut.GetEnabledToolsAsync();
        result1.Should().BeEquivalentTo(configs1);

        // Second call - should return cached values
        var result2 = await sut.GetEnabledToolsAsync();
        result2.Should().BeEquivalentTo(configs1);

        _configServiceMock.Verify(x => x.GetEnabledToolsAsync(), Times.Once);
    }

    [Fact]
    public async Task GetEnabledToolsAsync_Refreshes_WhenIntervalElapsed()
    {
        var configs1 = new[] { CreateConfig("1") };
        var configs2 = new[] { CreateConfig("2") };
        
        _configServiceMock.SetupSequence(x => x.GetEnabledToolsAsync())
            .ReturnsAsync(configs1)
            .ReturnsAsync(configs2);

        // Set refresh interval to 1 millisecond
        var sut = new McpToolManager(_configServiceMock.Object, TimeSpan.FromMilliseconds(1), NullLogger<McpToolManager>.Instance);

        // First call
        var result1 = await sut.GetEnabledToolsAsync();
        result1.Should().BeEquivalentTo(configs1);

        // Wait for interval to elapse
        await Task.Delay(5);

        // Second call - should hit the provider again
        var result2 = await sut.GetEnabledToolsAsync();
        result2.Should().BeEquivalentTo(configs2);

        _configServiceMock.Verify(x => x.GetEnabledToolsAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task RefreshToolsAsync_Exception_LogsWarningAndSetsEmptyCache()
    {
        var exception = new Exception("Connection timeout");
        _configServiceMock.Setup(x => x.GetEnabledToolsAsync()).ThrowsAsync(exception);

        var sut = new McpToolManager(_configServiceMock.Object, TimeSpan.FromSeconds(30), _loggerMock.Object);

        // Call GetEnabledToolsAsync which triggers RefreshToolsAsync internally
        var result = await sut.GetEnabledToolsAsync();

        result.Should().BeEmpty();
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to refresh MCP tools")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static McpToolConfiguration CreateConfig(string toolId) =>
        McpToolConfiguration.Rehydrate(
            id: Guid.NewGuid(),
            toolId: toolId,
            name: toolId,
            description: null,
            serverUrl: new Uri("http://localhost:8000"),
            toolType: "search",
            version: null,
            isSystemTool: false,
            priority: 0,
            timeoutMs: 30000,
            retryOnFailure: true,
            maxRetries: 3,
            createdAt: DateTime.UtcNow,
            isEnabled: true,
            configurationJson: null,
            disabledReason: null,
            lastTestedAt: null,
            lastConnectionStatus: null,
            updatedAt: null);
}
