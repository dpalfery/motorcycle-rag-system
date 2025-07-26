using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using MotorcycleRAG.Core.Services;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="MotorcycleRAGService"/>
/// </summary>
public class MotorcycleRAGServiceTests
{
    private readonly Mock<IAgentOrchestrator> _mockOrchestrator;
    private readonly Mock<ILogger<MotorcycleRAGService>> _mockLogger;
    private readonly MotorcycleRAGService _service;

    public MotorcycleRAGServiceTests()
    {
        _mockOrchestrator = new Mock<IAgentOrchestrator>(MockBehavior.Strict);
        _mockLogger = new Mock<ILogger<MotorcycleRAGService>>();
        _service = new MotorcycleRAGService(_mockOrchestrator.Object, _mockLogger.Object);
    }

    #region Constructor

    [Fact]
    public void Constructor_ShouldThrow_WhenOrchestratorIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MotorcycleRAGService(null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenLoggerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MotorcycleRAGService(_mockOrchestrator.Object, null!));
    }

    #endregion

    #region QueryAsync

    [Fact]
    public async Task QueryAsync_ShouldThrow_WhenRequestIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.QueryAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task QueryAsync_ShouldThrow_WhenQueryIsEmpty(string query)
    {
        var request = new MotorcycleQueryRequest { Query = query };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.QueryAsync(request));
    }

    [Fact]
    public async Task QueryAsync_ShouldReturnResponse_WhenValidRequest()
    {
        // Arrange
        var results = new[]
        {
            new SearchResult
            {
                Id = "1",
                Content = "Test content",
                RelevanceScore = 0.9f,
                Source = new SearchSource
                {
                    AgentType = SearchAgentType.VectorSearch,
                    SourceName = "Test",
                    DocumentId = "doc1"
                }
            }
        };

        _mockOrchestrator.Setup(o => o.ExecuteSequentialSearchAsync(It.IsAny<string>(), It.IsAny<SearchContext>()))
                          .ReturnsAsync(results);

        _mockOrchestrator.Setup(o => o.GenerateResponseAsync(results, It.IsAny<string>()))
                          .ReturnsAsync("Final answer");

        var request = new MotorcycleQueryRequest { Query = "Tell me about the Honda CBR1000RR" };

        // Act
        var response = await _service.QueryAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("Final answer", response.Response);
        Assert.Equal(results.Length, response.Sources.Length);
        Assert.Equal(results.Length, response.Metrics.ResultsFound);
        Assert.False(string.IsNullOrWhiteSpace(response.QueryId));

        _mockOrchestrator.Verify(o => o.ExecuteSequentialSearchAsync(request.Query, It.IsAny<SearchContext>()), Times.Once);
        _mockOrchestrator.Verify(o => o.GenerateResponseAsync(results, request.Query), Times.Once);
    }

    #endregion

    #region GetHealthAsync

    [Fact]
    public async Task GetHealthAsync_ShouldReturnHealthyResult()
    {
        // Act
        var result = await _service.GetHealthAsync();

        // Assert
        Assert.True(result.IsHealthy);
        Assert.Equal("OK", result.Status);
        Assert.NotEmpty(result.Details);
    }

    #endregion
}