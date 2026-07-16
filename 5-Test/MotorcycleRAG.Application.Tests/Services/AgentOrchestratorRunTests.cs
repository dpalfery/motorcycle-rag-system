using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Agents.Orchestration;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Application.Services.QueryValidation;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services;

public class AgentOrchestratorRunTests
{
    private static readonly IDisposable LoggingScope = new NoopDisposable();

    private readonly Mock<IFoundryAgentRunner> _mockRunner = new(MockBehavior.Strict);
    private readonly Mock<ILogger<AgentOrchestrator>> _mockLogger = new();
    private readonly Mock<ILogger<FoundryToolDispatcher>> _mockDispatcherLogger = new();
    private readonly Mock<IOptions<AzureFoundryOptions>> _mockOptions = new();
    private readonly Mock<ICorrelationService> _mockCorrelation = new();
    private readonly QuestionValidationState _questionValidationState = new();

    private const string ConversationId = "conversation-abc";
    private const string ResponseId = "response-xyz";
    private const string AgentName = "MCR-OrchestratorAgent";

    public AgentOrchestratorRunTests()
    {
        var options = new AzureFoundryOptions
        {
            OrchestratorAgentName = AgentName,
            VectorSearchAgentName = "MCR-VectorSearchAgent",
            WebSearchAgentName = "MCR-WebSearchAgent",
            PDFSearchAgentName = "MCR-PDFSearchAgent"
        };
        _mockOptions.Setup(o => o.Value).Returns(options);
    }

    private AgentOrchestrator CreateOrchestrator()
    {
        _mockCorrelation
            .Setup(c => c.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(LoggingScope);
        var dispatcher = new FoundryToolDispatcher(_mockDispatcherLogger.Object);
        return new AgentOrchestrator(
            [],
            _mockLogger.Object,
            _mockRunner.Object,
            dispatcher,
            _mockOptions.Object,
            _mockCorrelation.Object,
            _questionValidationState);
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task ExecuteSequentialSearchAsync_ResponseCompletesImmediately_ReturnsAnswer()
    {
        var completedStatus = new AgentResponseStatus(ResponseId, AgentRunState.Completed, null, "Final answer from Foundry");
        SetupBaseResponseFlow(completedStatus);
        _mockRunner.Setup(r => r.DeleteConversationAsync(ConversationId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateOrchestrator();

        var results = await sut.ExecuteSequentialSearchAsync("best motorcycle", new SearchContext());

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Content.Contains("Final answer from Foundry"));
    }

    [Fact]
    public async Task ExecuteSequentialSearchAsync_OneRequiresActionCycle_ReturnsAnswer()
    {
        var toolCalls = new List<AgentToolCall> { new("call-1", "vector_search", "{\"query\":\"test\",\"max_results\":10}") };
        var requiresAction = new AgentResponseStatus(ResponseId, AgentRunState.RequiresAction, toolCalls, string.Empty);
        var completed = new AgentResponseStatus(ResponseId, AgentRunState.Completed, null, "Final answer after tool call");

        SetupBaseResponseFlow(requiresAction);
        _mockRunner.Setup(r => r.SubmitToolOutputsAsync(
                ConversationId, AgentName, It.IsAny<IEnumerable<AgentToolOutput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(completed);
        _mockRunner.Setup(r => r.DeleteConversationAsync(ConversationId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateOrchestrator();

        var results = await sut.ExecuteSequentialSearchAsync("best motorcycle", new SearchContext());

        Assert.NotEmpty(results);
        _mockRunner.Verify(r => r.SubmitToolOutputsAsync(
            ConversationId, AgentName, It.IsAny<IEnumerable<AgentToolOutput>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteSequentialSearchAsync_FailedResponse_ThrowsInvalidOperationException()
    {
        var failedStatus = new AgentResponseStatus(ResponseId, AgentRunState.Failed, null, string.Empty);
        SetupBaseResponseFlow(failedStatus);
        _mockRunner.Setup(r => r.DeleteConversationAsync(ConversationId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateOrchestrator();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ExecuteSequentialSearchAsync("test", new SearchContext()));
    }

    [Fact]
    public async Task ExecuteSequentialSearchAsync_EmptyQuery_ReturnsEmptyWithoutCallingFoundry()
    {
        var sut = CreateOrchestrator();

        var results = await sut.ExecuteSequentialSearchAsync("   ", new SearchContext());

        Assert.Empty(results);
        _mockRunner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GenerateResponseAsync_EmptyResults_ReturnsEmpty()
    {
        var sut = CreateOrchestrator();
        var answer = await sut.GenerateResponseAsync([], "query");
        Assert.Equal(string.Empty, answer);
    }

    [Fact]
    public async Task GenerateResponseAsync_WithFoundryAnswerResult_ReturnsFoundryContent()
    {
        var sut = CreateOrchestrator();
        var results = new[]
        {
            new SearchResult
            {
                Id = "foundry",
                Content = "Foundry synthesized answer",
                Metadata = new Dictionary<string, object> { ["FoundryAnswer"] = true }
            }
        };

        var answer = await sut.GenerateResponseAsync(results, "query");

        answer.Should().Be("Foundry synthesized answer");
    }

    [Fact]
    public async Task GenerateResponseAsync_WithoutFoundryAnswer_FallsBackToConcatenation()
    {
        var sut = CreateOrchestrator();
        var results = new[]
        {
            new SearchResult { Id = "r1", Content = "Answer one" },
            new SearchResult { Id = "r2", Content = "Answer two" }
        };

        var answer = await sut.GenerateResponseAsync(results, "query");

        answer.Should().Be("Answer one\n\nAnswer two");
    }

    [Fact]
    public async Task OrchestrateSearchAsync_EmptyQuery_ReturnsEmptyResults()
    {
        var sut = CreateOrchestrator();

        var results = await sut.OrchestrateSearchAsync("   ", new SearchParameters { MaxResults = 10 });

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task OrchestrateSearchAsync_NullOptions_ThrowsArgumentNullException()
    {
        var sut = CreateOrchestrator();

        var act = () => sut.OrchestrateSearchAsync("query", null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task OrchestrateSearchAsync_WithValidQuery_ExecutesSearchAndReturnsResults()
    {
        var completedStatus = new AgentResponseStatus(ResponseId, AgentRunState.Completed, null, "Final answer");
        SetupBaseResponseFlow(completedStatus);
        _mockRunner.Setup(r => r.DeleteConversationAsync(ConversationId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var sut = CreateOrchestrator();

        var results = await sut.OrchestrateSearchAsync("best motorcycle", new SearchParameters { MaxResults = 10, MinRelevanceScore = 0.5F });

        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.Content.Contains("Final answer"));
    }

    [Fact]
    public async Task GetAvailableAgents_ReturnsInjectedAgents()
    {
        var agent = new Mock<ISearchAgent>().Object;
        var sut = new AgentOrchestrator(
            [agent],
            _mockLogger.Object,
            _mockRunner.Object,
            new FoundryToolDispatcher(_mockDispatcherLogger.Object),
            _mockOptions.Object,
            _mockCorrelation.Object,
            _questionValidationState);

        var agents = sut.GetAvailableAgents();

        agents.Should().ContainSingle().Which.Should().BeSameAs(agent);
    }

    [Fact]
    public async Task ExecuteSequentialSearchAsync_WithRecentMessages_BuildsContextMessage()
    {
        var completedStatus = new AgentResponseStatus(ResponseId, AgentRunState.Completed, null, "Final answer");
        SetupBaseResponseFlow(completedStatus);
        _mockRunner.Setup(r => r.DeleteConversationAsync(ConversationId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateOrchestrator();
        var context = new SearchContext
        {
            QueryContext = new QueryContext()
        };
        context.QueryContext.RecentMessages.Add(new QueryRecentMessage { Role = "user", Content = "Hello" });
        context.QueryContext.RecentMessages.Add(new QueryRecentMessage { Role = "assistant", Content = "Hi there" });

        var results = await sut.ExecuteSequentialSearchAsync("best motorcycle", context);

        results.Should().NotBeEmpty();
        _mockRunner.Verify(r => r.SendAgentMessageAsync(
            ConversationId,
            AgentName,
            It.Is<string>(m => m.Contains("Recent conversation context") && m.Contains("Hello")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteSequentialSearchAsync_SafeDeleteConversationFailure_DoesNotThrow()
    {
        var completedStatus = new AgentResponseStatus(ResponseId, AgentRunState.Completed, null, "Final answer");
        SetupBaseResponseFlow(completedStatus);
        _mockRunner.Setup(r => r.DeleteConversationAsync(ConversationId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("delete failed"));

        var sut = CreateOrchestrator();

        var results = await sut.ExecuteSequentialSearchAsync("query", new SearchContext());

        results.Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_NullAgents_ThrowsArgumentNullException()
    {
        var act = () => new AgentOrchestrator(
            null!,
            _mockLogger.Object,
            _mockRunner.Object,
            new FoundryToolDispatcher(_mockDispatcherLogger.Object),
            _mockOptions.Object,
            _mockCorrelation.Object,
            _questionValidationState);

        act.Should().Throw<ArgumentNullException>().WithParameterName("agents");
    }

    private void SetupBaseResponseFlow(AgentResponseStatus responseStatus)
    {
        _mockRunner.Setup(r => r.CreateConversationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationId);
        _mockRunner.Setup(r => r.SendAgentMessageAsync(ConversationId, AgentName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseStatus);
    }
}
