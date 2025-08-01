using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Core.Agents;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using Xunit;

namespace MotorcycleRAG.UnitTests.Agents;

public class QueryPlannerAgentTests : IDisposable
{
    private readonly Mock<IAzureOpenAIClient> _mockOpenAIClient;
    private readonly Mock<ISearchAgent> _mockVectorAgent;
    private readonly Mock<ISearchAgent> _mockWebAgent;
    private readonly IOptions<AzureAIConfiguration> _config;
    private readonly Mock<ILogger<QueryPlannerAgent>> _mockLogger;
    private readonly QueryPlannerAgent _planner;

    public QueryPlannerAgentTests()
    {
        _mockOpenAIClient = new Mock<IAzureOpenAIClient>();
        _mockVectorAgent = new Mock<ISearchAgent>();
        _mockVectorAgent.SetupGet(a => a.AgentType).Returns(SearchAgentType.VectorSearch);
        _mockWebAgent = new Mock<ISearchAgent>();
        _mockWebAgent.SetupGet(a => a.AgentType).Returns(SearchAgentType.WebSearch);
        _mockLogger = new Mock<ILogger<QueryPlannerAgent>>();

        _config = Options.Create(new AzureAIConfiguration
        {
            FoundryEndpoint = "https://test",
            OpenAIEndpoint = "https://test",
            SearchServiceEndpoint = "https://test",
            DocumentIntelligenceEndpoint = "https://test",
            Models = new ModelConfiguration { QueryPlannerModel = "gpt-4o" }
        });

        _planner = new QueryPlannerAgent(
            _mockOpenAIClient.Object,
            new[] { _mockVectorAgent.Object, _mockWebAgent.Object },
            _config,
            _mockLogger.Object);
    }

    [Fact]
    public void AgentType_ShouldReturnQueryPlanner()
    {
        Assert.Equal(SearchAgentType.QueryPlanner, _planner.AgentType);
    }

    [Fact]
    public async Task SearchAsync_ShouldExecuteVectorAndWebSearch_WhenPlanIndicatesWebSearch()
    {
        var planJson = "{\"subQueries\":[\"query1\"],\"useWebSearch\":true,\"runParallel\":true}";
        _mockOpenAIClient.Setup(x => x.GetChatCompletionAsync("gpt-4o", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(planJson);

        _mockVectorAgent.Setup(x => x.SearchAsync("query1", It.IsAny<SearchOptions>()))
            .ReturnsAsync(new[]
            {
                new SearchResult
                {
                    Id = "v1",
                    Content = "vector",
                    RelevanceScore = 0.9f,
                    Source = new SearchSource{AgentType = SearchAgentType.VectorSearch, SourceName="vector"}
                }
            });

        _mockWebAgent.Setup(x => x.SearchAsync("query1", It.IsAny<SearchOptions>()))
            .ReturnsAsync(new[]
            {
                new SearchResult
                {
                    Id = "w1",
                    Content = "web",
                    RelevanceScore = 0.8f,
                    Source = new SearchSource{AgentType = SearchAgentType.WebSearch, SourceName="web"}
                }
            });

        var results = await _planner.SearchAsync("test query", new SearchOptions());

        Assert.Equal(2, results.Length);
        _mockVectorAgent.Verify(x => x.SearchAsync("query1", It.IsAny<SearchOptions>()), Times.Once);
        _mockWebAgent.Verify(x => x.SearchAsync("query1", It.IsAny<SearchOptions>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmptyArray_WhenQueryIsEmpty()
    {
        var results = await _planner.SearchAsync(string.Empty, new SearchOptions());
        Assert.Empty(results);
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenDependenciesNull()
    {
        Assert.Throws<ArgumentNullException>(() => new QueryPlannerAgent(null!, new[] { _mockVectorAgent.Object }, _config, _mockLogger.Object));
        Assert.Throws<ArgumentNullException>(() => new QueryPlannerAgent(_mockOpenAIClient.Object, null!, _config, _mockLogger.Object));
        Assert.Throws<ArgumentNullException>(() => new QueryPlannerAgent(_mockOpenAIClient.Object, new[] { _mockVectorAgent.Object }, null!, _mockLogger.Object));
        Assert.Throws<ArgumentNullException>(() => new QueryPlannerAgent(_mockOpenAIClient.Object, new[] { _mockVectorAgent.Object }, _config, null!));
    }

    public void Dispose()
    {
        // nothing to dispose
    }
}
