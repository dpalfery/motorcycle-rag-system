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
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
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
            .ReturnsAsync((IReadOnlyList<GraphNode>)new List<GraphNode>());

        var result = await _handlers.HandleSearchGraphNodesAsync(call, CancellationToken.None);

        Assert.NotNull(result);
        _graphRepoMock.Verify(x => x.SearchNodesAsync("honda", null, 10, It.IsAny<CancellationToken>()), Times.Once);
    }
}
