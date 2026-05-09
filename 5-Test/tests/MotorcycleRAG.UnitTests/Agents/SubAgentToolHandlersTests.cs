using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Application.Agents.Orchestration;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
using System.Text.Json;
using Xunit;

namespace MotorcycleRAG.UnitTests.Agents;

public class SubAgentToolHandlersTests
{
    private readonly Mock<IAzureSearchClient> _mockSearchClient = new(MockBehavior.Strict);
    private readonly Mock<ITrustedSourcesLoader> _mockLoader = new(MockBehavior.Strict);
    private readonly Mock<IGraphRepository> _mockGraphRepo = new(MockBehavior.Strict);
    private readonly Mock<ILogger<SubAgentToolHandlers>> _mockLogger = new();

    private SubAgentToolHandlers CreateHandlers() =>
        new(_mockSearchClient.Object, _mockLoader.Object, _mockGraphRepo.Object, _mockLogger.Object);

    [Fact]
    public async Task HandleExecuteAzureSearchAsync_CallsSearchClient_ReturnsJson()
    {
        // Arrange
        var results = new[]
        {
            new SearchResult
            {
                Id = "doc1",
                Content = "Honda CBR1000RR specs",
                RelevanceScore = 0.9f,
                Source = new SearchSource { SourceName = "Test", AgentType = SearchAgentType.VectorSearch, DocumentId = "d1" }
            }
        };
        _mockSearchClient
            .Setup(c => c.SearchAsync(It.IsAny<string>(), It.IsAny<SearchOptions>()))
            .ReturnsAsync(results);

        var call = new AgentToolCall("call-1", "execute_azure_search",
            JsonSerializer.Serialize(new { query = "Honda CBR1000RR", max_results = 5 }));
        var handlers = CreateHandlers();

        // Act
        var output = await handlers.HandleExecuteAzureSearchAsync(call, CancellationToken.None);

        // Assert
        Assert.Equal("call-1", output.CallId);
        Assert.Contains("Honda CBR1000RR", output.Output);
        _mockSearchClient.Verify(c => c.SearchAsync(It.IsAny<string>(), It.IsAny<SearchOptions>()), Times.Once);
    }

    [Fact]
    public async Task HandleScoreContentAsync_TierOne_ReturnsHighScore()
    {
        // Arrange
        var call = new AgentToolCall("call-2", "score_content",
            JsonSerializer.Serialize(new { content = "Good content", source_url = "https://example.com", trust_tier = 1 }));
        var handlers = CreateHandlers();

        // Act
        var output = await handlers.HandleScoreContentAsync(call, CancellationToken.None);

        // Assert
        Assert.Equal("call-2", output.CallId);
        var scoreDoc = JsonSerializer.Deserialize<JsonElement>(output.Output);
        Assert.True(scoreDoc.GetProperty("score").GetDouble() >= 0.9);
    }

    [Fact]
    public async Task HandleScoreContentAsync_TierFive_ReturnsLowScore()
    {
        // Arrange
        var call = new AgentToolCall("call-3", "score_content",
            JsonSerializer.Serialize(new { content = "Low trust content", source_url = "https://low.com", trust_tier = 5 }));
        var handlers = CreateHandlers();

        // Act
        var output = await handlers.HandleScoreContentAsync(call, CancellationToken.None);

        // Assert
        var scoreDoc = JsonSerializer.Deserialize<JsonElement>(output.Output);
        Assert.True(scoreDoc.GetProperty("score").GetDouble() <= 0.4);
    }

    [Fact]
    public async Task HandleGetTrustedSourcesAsync_CallsLoader_ReturnsMappedSources()
    {
        // Arrange
        var sources = new[]
        {
            new TrustedSourceOptions
            {
                Name = "BikeMag",
                BaseUrl = new Uri("https://bikemag.com"),
                SearchUrlTemplate = new Uri("https://bikemag.com/search?q={query}"),
                ContentSelector = "//article",
                CredibilityScore = 0.95f
            }
        };
        _mockLoader.Setup(l => l.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(sources);

        var call = new AgentToolCall("call-4", "get_trusted_sources", "{}");
        var handlers = CreateHandlers();

        // Act
        var output = await handlers.HandleGetTrustedSourcesAsync(call, CancellationToken.None);

        // Assert
        Assert.Equal("call-4", output.CallId);
        Assert.Contains("BikeMag", output.Output);
        _mockLoader.Verify(l => l.LoadAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleSearchPdfIndexAsync_CallsSearchClient_ReturnsJson()
    {
        // Arrange
        var results = new[]
        {
            new SearchResult
            {
                Id = "pdf1",
                Content = "Manual content",
                RelevanceScore = 0.85f,
                Source = new SearchSource { SourceName = "Manual", AgentType = SearchAgentType.PDFSearch, DocumentId = "p1" }
            }
        };
        _mockSearchClient
            .Setup(c => c.SearchAsync(It.IsAny<string>(), It.IsAny<SearchOptions>()))
            .ReturnsAsync(results);

        var call = new AgentToolCall("call-5", "search_pdf_index",
            JsonSerializer.Serialize(new { query = "oil change", max_results = 5 }));
        var handlers = CreateHandlers();

        // Act
        var output = await handlers.HandleSearchPdfIndexAsync(call, CancellationToken.None);

        // Assert
        Assert.Equal("call-5", output.CallId);
        Assert.Contains("Manual content", output.Output);
    }
}
