using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Application.Caching;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="MotorcycleRAGService"/>
/// </summary>
public class MotorcycleRAGServiceTests
{
    private readonly Mock<IAgentOrchestrator> _mockOrchestrator;
    private readonly Mock<ILogger<MotorcycleRAGService>> _mockLogger;
    private readonly Mock<ITelemetryService> _mockTelemetry;
    private readonly Mock<IQueryCacheService> _mockCacheService;
    private readonly Mock<Microsoft.Extensions.Options.IOptions<CacheConfiguration>> _mockCacheConfig;
    private readonly Mock<IAzureOpenAIClient> _mockOpenAIClient;
    private readonly MotorcycleRAGService _service;

    public MotorcycleRAGServiceTests()
    {
        _mockOrchestrator = new Mock<IAgentOrchestrator>(MockBehavior.Strict);
        _mockLogger = new Mock<ILogger<MotorcycleRAGService>>();
        _mockTelemetry = new Mock<ITelemetryService>();
        _mockCacheService = new Mock<IQueryCacheService>();
        _mockCacheConfig = new Mock<Microsoft.Extensions.Options.IOptions<CacheConfiguration>>();
        _mockOpenAIClient = new Mock<IAzureOpenAIClient>();
        _mockCacheConfig.Setup(x => x.Value).Returns(new CacheConfiguration { EnableCaching = true, DefaultExpiration = TimeSpan.FromMinutes(5) });

        // Set up OpenAI client mock to return empty JSON array for factual claims
        _mockOpenAIClient.Setup(o => o.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync("[]");

        _service = new MotorcycleRAGService(_mockOrchestrator.Object, _mockLogger.Object, _mockTelemetry.Object, _mockCacheService.Object, _mockCacheConfig.Object, _mockOpenAIClient.Object);
    }

    #region Constructor

    [Fact]
    public void Constructor_ShouldThrow_WhenOrchestratorIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MotorcycleRAGService(null!, _mockLogger.Object, _mockTelemetry.Object, _mockCacheService.Object, _mockCacheConfig.Object, _mockOpenAIClient.Object));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenLoggerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MotorcycleRAGService(_mockOrchestrator.Object, null!, _mockTelemetry.Object, _mockCacheService.Object, _mockCacheConfig.Object, _mockOpenAIClient.Object));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenTelemetryIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MotorcycleRAGService(_mockOrchestrator.Object, _mockLogger.Object, null!, _mockCacheService.Object, _mockCacheConfig.Object, _mockOpenAIClient.Object));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenOpenAIClientIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MotorcycleRAGService(_mockOrchestrator.Object, _mockLogger.Object, _mockTelemetry.Object, _mockCacheService.Object, _mockCacheConfig.Object, null!));
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
        _mockTelemetry.Verify(t => t.TrackQuery(It.IsAny<string>(), request.Query, It.IsAny<TimeSpan>(), results.Length, It.IsAny<decimal>()), Times.Once);
    }

    #endregion

    #region Answer Composition and Citation Tests

    [Fact]
    public async Task QueryAsync_ShouldReturnResponseWithCitations_WhenValidRequest()
    {
        // Arrange
        var results = new[]
        {
            new SearchResult
            {
                Id = "1",
                Content = "Honda CBR1000RR specifications: 1000cc inline-4 engine, 200 horsepower",
                RelevanceScore = 0.9f,
                Source = new SearchSource
                {
                    AgentType = SearchAgentType.VectorSearch,
                    SourceName = "Honda Official Specifications",
                    SourceUrl = "https://www.honda.com/cbr1000rr/specs",
                    DocumentId = "honda-cbr1000rr-2023",
                    LastUpdated = DateTime.UtcNow.AddDays(-30),
                    Citation = new Citation
                    {
                        SourceType = CitationSourceType.Dataset,
                        SourceName = "Honda Official Specifications",
                        SourceUrl = "https://www.honda.com/cbr1000rr/specs",
                        PageNumber = 1,
                        Section = "Engine Specifications",
                        ConfidenceScore = 0.95f,
                        Verified = true,
                        VerificationMethod = "Cross-referenced with manufacturer data",
                        Locator = new DatasetCitationLocator
                        {
                            DatasetName = "Honda Motorcycle Specifications 2023",
                            Version = "2023.1",
                            RecordId = "CBR1000RR-2023-001",
                            FieldName = "Engine.Horsepower",
                            DataSourceUrl = "https://www.honda.com/api/specs/v1/motorcycles",
                            RetrievalTimestamp = DateTime.UtcNow.AddDays(-1)
                        }
                    }
                },
                Metadata = new Dictionary<string, object>
                {
                    { "Brand", "Honda" },
                    { "Model", "CBR1000RR" },
                    { "Year", 2023 }
                }
            }
        };

        _mockOrchestrator.Setup(o => o.ExecuteSequentialSearchAsync(It.IsAny<string>(), It.IsAny<SearchContext>()))
                          .ReturnsAsync(results);

        _mockOrchestrator.Setup(o => o.GenerateResponseAsync(results, It.IsAny<string>()))
                          .ReturnsAsync("The Honda CBR1000RR has a 1000cc inline-4 engine producing 200 horsepower. [1]");

        var request = new MotorcycleQueryRequest { Query = "Tell me about the Honda CBR1000RR" };

        // Act
        var response = await _service.QueryAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.Contains("[1]", response.Response); // Verify citation marker is present
        Assert.Equal(results.Length, response.Sources.Length);
        Assert.All(response.Sources, s => Assert.NotNull(s.Source.Citation)); // All sources have citations
        Assert.False(string.IsNullOrWhiteSpace(response.QueryId));
        Assert.NotEqual(DateTime.MinValue, response.GeneratedAt);

        _mockOrchestrator.Verify(o => o.ExecuteSequentialSearchAsync(request.Query, It.IsAny<SearchContext>()), Times.Once);
        _mockOrchestrator.Verify(o => o.GenerateResponseAsync(results, request.Query), Times.Once);
        _mockTelemetry.Verify(t => t.TrackQuery(It.IsAny<string>(), request.Query, It.IsAny<TimeSpan>(), results.Length, It.IsAny<decimal>()), Times.Once);
    }

    [Fact]
    public async Task QueryAsync_ShouldHandleNoResultsWithRefinementSuggestions()
    {
        // Arrange
        var emptyResults = Array.Empty<SearchResult>();

        _mockOrchestrator.Setup(o => o.ExecuteSequentialSearchAsync(It.IsAny<string>(), It.IsAny<SearchContext>()))
                          .ReturnsAsync(emptyResults);

        _mockOrchestrator.Setup(o => o.GenerateResponseAsync(emptyResults, It.IsAny<string>()))
                          .ReturnsAsync(string.Empty); // Empty response triggers no-results handling

        var request = new MotorcycleQueryRequest { Query = "Tell me about some random topic" };

        // Act
        var response = await _service.QueryAsync(request);

        // Assert
        Assert.NotNull(response);
        // The implementation returns markdown format with "# No Results Found"
        Assert.Contains("No Results Found", response.Response);
        Assert.Contains("Suggestions to Improve Your Search", response.Response);
        Assert.Empty(response.Sources);
        Assert.Equal(0, response.Metrics.ResultsFound);
        Assert.False(string.IsNullOrWhiteSpace(response.QueryId));

        _mockOrchestrator.Verify(o => o.ExecuteSequentialSearchAsync(request.Query, It.IsAny<SearchContext>()), Times.Once);
        _mockOrchestrator.Verify(o => o.GenerateResponseAsync(emptyResults, request.Query), Times.Once);
    }

    [Fact]
    public async Task QueryAsync_ShouldReturnStableQueryIdAndCompleteMetrics()
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
                    DocumentId = "doc1",
                    Citation = new Citation
                    {
                        SourceType = CitationSourceType.Dataset,
                        SourceName = "Test Source",
                        Verified = true
                    }
                }
            }
        };

        _mockOrchestrator.Setup(o => o.ExecuteSequentialSearchAsync(It.IsAny<string>(), It.IsAny<SearchContext>()))
                          .ReturnsAsync(results);

        _mockOrchestrator.Setup(o => o.GenerateResponseAsync(results, It.IsAny<string>()))
                          .ReturnsAsync("Test answer");

        var request = new MotorcycleQueryRequest { Query = "Test query" };

        // Act
        var response = await _service.QueryAsync(request);

        // Assert - Verify stable query ID format
        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response.QueryId));
        Assert.DoesNotContain("-", response.QueryId); // GUID without hyphens (N format)
        Assert.Equal(32, response.QueryId.Length); // GUID length

        // Assert - Verify complete metrics
        Assert.NotNull(response.Metrics);
        // Note: TotalDuration may be very small but should be set
        Assert.True(response.Metrics.TotalDuration >= TimeSpan.Zero);
        Assert.Equal(results.Length, response.Metrics.ResultsFound);
        Assert.NotEqual(DateTime.MinValue, response.GeneratedAt);

        // Verify metrics include all expected fields
        // ProcessingTimeMs can be 0 for very fast executions, so we just check it's non-negative
        Assert.True(response.Metrics.ProcessingTimeMs >= 0);
        Assert.False(response.Metrics.CacheHit); // Should be false for first run
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
