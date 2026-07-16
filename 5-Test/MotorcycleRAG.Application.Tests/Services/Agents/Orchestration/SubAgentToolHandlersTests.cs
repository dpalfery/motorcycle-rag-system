using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services.Agents;
using MotorcycleRAG.Application.Services.Agents.Orchestration;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.Graph;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Agents.Orchestration;

public class SubAgentToolHandlersTests
{
    private readonly Mock<IAzureSearchClient> _searchClientMock;
    private readonly Mock<ITrustedSourcesLoader> _trustedSourcesMock;
    private readonly Mock<IGraphRepository> _graphRepoMock;
    private readonly Mock<ITrustedWebContentFetcher> _trustedWebContentFetcherMock;
    private readonly SubAgentToolHandlers _handlers;

    public SubAgentToolHandlersTests()
    {
        _searchClientMock = new Mock<IAzureSearchClient>();
        _trustedSourcesMock = new Mock<ITrustedSourcesLoader>();
        _graphRepoMock = new Mock<IGraphRepository>();
        _trustedWebContentFetcherMock = new Mock<ITrustedWebContentFetcher>();

        _handlers = new SubAgentToolHandlers(
            _searchClientMock.Object,
            _trustedSourcesMock.Object,
            _graphRepoMock.Object,
            _trustedWebContentFetcherMock.Object,
            NullLogger<SubAgentToolHandlers>.Instance
        );
    }

    [Fact]
    public void Constructor_NullDependencies_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SubAgentToolHandlers(null!, _trustedSourcesMock.Object, _graphRepoMock.Object, _trustedWebContentFetcherMock.Object, NullLogger<SubAgentToolHandlers>.Instance));
    }

    [Fact]
    public void RegisterOn_RegistersHandlers()
    {
        var dispatcher = new FoundryToolDispatcher(NullLogger<FoundryToolDispatcher>.Instance);
        _handlers.RegisterOn(dispatcher);
    }

    [Fact]
    public async Task HandleExecuteAzureSearchAsync_ValidArguments_CallsSearchAndReturnsResults()
    {
        var call = new AgentToolCall("call_123", "execute_azure_search", "{\"query\":\"test\", \"max_results\":5}");
        
        _searchClientMock.Setup(x => x.SearchAsync("test", It.IsAny<SearchOptions>()))
            .ReturnsAsync(Array.Empty<SearchResult>());

        var result = await _handlers.HandleExecuteAzureSearchAsync(call, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("call_123", result.CallId);
        _searchClientMock.Verify(x => x.SearchAsync("test", It.IsAny<SearchOptions>()), Times.Once);
    }

    [Fact]
    public async Task HandleScoreContentAsync_ReturnsScore()
    {
        var call = new AgentToolCall("call_abc", "score_content", "{\"trust_tier\": 1}");
        var result = await _handlers.HandleScoreContentAsync(call, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("0.95", result.Output);
    }

    [Fact]
    public async Task HandleFetchWebContentAsync_WhenFetcherSucceeds_DelegatesAndReturnsTruncatedContent()
    {
        var content = new string('x', 2001);
        var call = new AgentToolCall("call_web", "fetch_web_content", "{\"url\":\"https://example.com/article\",\"search_term\":\"Honda\"}");
        _trustedWebContentFetcherMock
            .Setup(fetcher => fetcher.FetchAsync(new Uri("https://example.com/article"), "Honda", It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);

        var result = await _handlers.HandleFetchWebContentAsync(call, CancellationToken.None);

        Assert.Contains(new string('x', 2000), result.Output);
        _trustedWebContentFetcherMock.Verify(
            fetcher => fetcher.FetchAsync(new Uri("https://example.com/article"), "Honda", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleFetchWebContentAsync_WhenFetcherFails_ReturnsErrorPayload()
    {
        var call = new AgentToolCall("call_web_failure", "fetch_web_content", "{\"url\":\"https://example.com/article\",\"search_term\":\"Honda\"}");
        _trustedWebContentFetcherMock
            .Setup(fetcher => fetcher.FetchAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("upstream unavailable"));

        var result = await _handlers.HandleFetchWebContentAsync(call, CancellationToken.None);

        Assert.Contains("upstream unavailable", result.Output);
    }

    [Fact]
    public async Task HandleSearchGraphNodesAsync_CallsRepository()
    {
        var call = new AgentToolCall("call_graph", "search_graph_nodes", "{\"search_term\":\"honda\"}");
        
        _graphRepoMock.Setup(x => x.SearchNodesAsync("honda", null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<GraphNodeDto>)new List<GraphNodeDto>());

        var result = await _handlers.HandleSearchGraphNodesAsync(call, CancellationToken.None);

        Assert.NotNull(result);
        _graphRepoMock.Verify(x => x.SearchNodesAsync("honda", null, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleGetNeighboursAsync_InvalidNodeId_ReturnsErrorPayload()
    {
        var call = new AgentToolCall("call_neighbours", "get_neighbours", "{\"node_id\":\"not-a-guid\"}");

        var result = await _handlers.HandleGetNeighboursAsync(call, CancellationToken.None);

        Assert.Equal("call_neighbours", result.CallId);
        Assert.Contains("Invalid node_id", result.Output);
        _graphRepoMock.Verify(x => x.GetNeighboursAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleGetNeighboursAsync_ValidNodeId_ReturnsMappedRelationships()
    {
        var nodeId = Guid.NewGuid();
        var fromNode = new GraphNodeDto { Id = nodeId, Name = "Engine", Type = "Component" };
        var toNode = new GraphNodeDto { Id = Guid.NewGuid(), Name = "Oil Filter", Type = "Component" };
        var traversal = new GraphTraversalResultDto(fromNode, "REQUIRES", 0.9, "context", toNode);
        _graphRepoMock.Setup(x => x.GetNeighboursAsync(nodeId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GraphTraversalResultDto> { traversal });

        var call = new AgentToolCall("call_neighbours", "get_neighbours", $"{{\"node_id\":\"{nodeId}\"}}");
        var result = await _handlers.HandleGetNeighboursAsync(call, CancellationToken.None);

        Assert.Contains("Engine", result.Output);
        Assert.Contains("Oil Filter", result.Output);
        Assert.Contains("REQUIRES", result.Output);
        _graphRepoMock.Verify(x => x.GetNeighboursAsync(nodeId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleGetNeighboursAsync_WithRelationshipFilter_PassesFilter()
    {
        var nodeId = Guid.NewGuid();
        _graphRepoMock.Setup(x => x.GetNeighboursAsync(nodeId, "REQUIRES", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GraphTraversalResultDto>());

        var call = new AgentToolCall("call_neighbours", "get_neighbours", $"{{\"node_id\":\"{nodeId}\",\"relationship_type_filter\":\"REQUIRES\"}}");
        await _handlers.HandleGetNeighboursAsync(call, CancellationToken.None);

        _graphRepoMock.Verify(x => x.GetNeighboursAsync(nodeId, "REQUIRES", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleFindPathsAsync_InvalidSourceNodeId_ReturnsErrorPayload()
    {
        var call = new AgentToolCall("call_paths", "find_paths", "{\"source_node_id\":\"not-a-guid\"}");

        var result = await _handlers.HandleFindPathsAsync(call, CancellationToken.None);

        Assert.Contains("Invalid source_node_id", result.Output);
        _graphRepoMock.Verify(x => x.FindPathsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleFindPathsAsync_ValidSourceNodeId_ReturnsMappedPaths()
    {
        var sourceId = Guid.NewGuid();
        var intermediateId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var path = new GraphPathResultDto(
            new GraphNodeDto { Id = sourceId, Name = "Battery", Type = "Component" },
            "POWERS",
            new GraphNodeDto { Id = intermediateId, Name = "Starter Motor", Type = "Component" },
            "DRIVES",
            new GraphNodeDto { Id = targetId, Name = "Engine", Type = "Component" });
        _graphRepoMock.Setup(x => x.FindPathsAsync(sourceId, 3, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GraphPathResultDto> { path });

        var call = new AgentToolCall("call_paths", "find_paths", $"{{\"source_node_id\":\"{sourceId}\"}}");
        var result = await _handlers.HandleFindPathsAsync(call, CancellationToken.None);

        Assert.Contains("Battery", result.Output);
        Assert.Contains("Starter Motor", result.Output);
        Assert.Contains("Engine", result.Output);
        Assert.Contains("POWERS", result.Output);
        Assert.Contains("DRIVES", result.Output);
        _graphRepoMock.Verify(x => x.FindPathsAsync(sourceId, 3, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleFindPathsAsync_CustomDepthAndResults_PassesValues()
    {
        var sourceId = Guid.NewGuid();
        _graphRepoMock.Setup(x => x.FindPathsAsync(sourceId, 5, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GraphPathResultDto>());

        var call = new AgentToolCall("call_paths", "find_paths", $"{{\"source_node_id\":\"{sourceId}\",\"max_depth\":5,\"max_results\":10}}");
        await _handlers.HandleFindPathsAsync(call, CancellationToken.None);

        _graphRepoMock.Verify(x => x.FindPathsAsync(sourceId, 5, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleGetEdgesByTypeAsync_CallsRepositoryAndReturnsMappedEdges()
    {
        var fromNode = new GraphNodeDto { Id = Guid.NewGuid(), Name = "A", Type = "Component" };
        var toNode = new GraphNodeDto { Id = Guid.NewGuid(), Name = "B", Type = "Component" };
        var edge = new GraphTraversalResultDto(fromNode, "RELATED_TO", 0.8, null, toNode);
        _graphRepoMock.Setup(x => x.GetEdgesByTypeAsync("RELATED_TO", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GraphTraversalResultDto> { edge });

        var call = new AgentToolCall("call_edges", "get_edges_by_type", "{\"relationship_type\":\"RELATED_TO\"}");
        var result = await _handlers.HandleGetEdgesByTypeAsync(call, CancellationToken.None);

        Assert.Contains("RELATED_TO", result.Output);
        Assert.Contains("A", result.Output);
        Assert.Contains("B", result.Output);
        _graphRepoMock.Verify(x => x.GetEdgesByTypeAsync("RELATED_TO", 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleGetEdgesByTypeAsync_CustomMaxResults_PassesValue()
    {
        _graphRepoMock.Setup(x => x.GetEdgesByTypeAsync("X", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GraphTraversalResultDto>());

        var call = new AgentToolCall("call_edges", "get_edges_by_type", "{\"relationship_type\":\"X\",\"max_results\":25}");
        await _handlers.HandleGetEdgesByTypeAsync(call, CancellationToken.None);

        _graphRepoMock.Verify(x => x.GetEdgesByTypeAsync("X", 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(1, "0.95")]
    [InlineData(2, "0.8")]
    [InlineData(3, "0.65")]
    [InlineData(4, "0.5")]
    [InlineData(5, "0.35")]
    [InlineData(99, "0.5")]
    public async Task HandleScoreContentAsync_VariousTiers_ReturnsExpectedScore(int tier, string expectedScore)
    {
        var call = new AgentToolCall("call_abc", "score_content", $"{{\"trust_tier\": {tier}}}");
        var result = await _handlers.HandleScoreContentAsync(call, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(expectedScore, result.Output);
    }

    [Fact]
    public async Task HandleExecuteAzureSearchAsync_EmptyArguments_UsesDefaults()
    {
        var call = new AgentToolCall("call_empty", "execute_azure_search", "   ");
        _searchClientMock.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<SearchOptions>())).ReturnsAsync(Array.Empty<SearchResult>());

        var result = await _handlers.HandleExecuteAzureSearchAsync(call, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task HandleExecuteAzureSearchAsync_InvalidArgumentsJson_UsesDefaults()
    {
        var call = new AgentToolCall("call_invalid", "execute_azure_search", "{not json");
        _searchClientMock.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<SearchOptions>())).ReturnsAsync(Array.Empty<SearchResult>());

        var result = await _handlers.HandleExecuteAzureSearchAsync(call, CancellationToken.None);

        Assert.NotNull(result);
    }
}
