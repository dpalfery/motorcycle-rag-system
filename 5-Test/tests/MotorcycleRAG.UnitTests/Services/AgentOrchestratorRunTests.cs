using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Agents.Orchestration;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services;

public class AgentOrchestratorRunTests
{
    private readonly Mock<IFoundryAgentRunner> _mockRunner = new(MockBehavior.Strict);
    private readonly Mock<ILogger<AgentOrchestrator>> _mockLogger = new();
    private readonly Mock<ILogger<FoundryToolDispatcher>> _mockDispatcherLogger = new();
    private readonly Mock<IOptions<AzureFoundryOptions>> _mockOptions = new();

    private const string ThreadId = "thread-abc";
    private const string RunId = "run-xyz";
    private const string AgentId = "orchestrator-agent-id";

    public AgentOrchestratorRunTests()
    {
        var options = new AzureFoundryOptions
        {
            OrchestratorAgentId = AgentId,
            VectorSearchAgentId = "vs-agent",
            WebSearchAgentId = "ws-agent",
            PDFSearchAgentId = "pdf-agent"
        };
        _mockOptions.Setup(o => o.Value).Returns(options);
    }

    private AgentOrchestrator CreateOrchestrator()
    {
        var dispatcher = new FoundryToolDispatcher(_mockDispatcherLogger.Object);
        return new AgentOrchestrator(
            [],
            _mockLogger.Object,
            _mockRunner.Object,
            dispatcher,
            _mockOptions.Object);
    }

    [Fact]
    public async Task ExecuteSequentialSearchAsync_RunCompletesImmediately_ReturnsAnswer()
    {
        // Arrange
        var completedStatus = new AgentRunStatus(RunId, AgentRunState.Completed, null);
        SetupBaseRunFlow(completedStatus);
        _mockRunner.Setup(r => r.GetLastAssistantMessageAsync(ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Final answer from Foundry");
        _mockRunner.Setup(r => r.DeleteThreadAsync(ThreadId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateOrchestrator();

        // Act
        var results = await sut.ExecuteSequentialSearchAsync("best motorcycle", new SearchContext());

        // Assert — answer is embedded in results
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Content.Contains("Final answer from Foundry"));
    }

    [Fact]
    public async Task ExecuteSequentialSearchAsync_OneRequiresActionCycle_ReturnsAnswer()
    {
        // Arrange
        var toolCalls = new List<AgentToolCall> { new("call-1", "vector_search", "{\"query\":\"test\",\"max_results\":10}") };
        var requiresAction = new AgentRunStatus(RunId, AgentRunState.RequiresAction, toolCalls);
        var completed = new AgentRunStatus(RunId, AgentRunState.Completed, null);

        SetupBaseRunFlow(requiresAction);
        _mockRunner.Setup(r => r.SubmitToolOutputsAsync(
                ThreadId, RunId, It.IsAny<IEnumerable<AgentToolOutput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(completed);
        _mockRunner.Setup(r => r.GetLastAssistantMessageAsync(ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Final answer after tool call");
        _mockRunner.Setup(r => r.DeleteThreadAsync(ThreadId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateOrchestrator();

        // Act
        var results = await sut.ExecuteSequentialSearchAsync("best motorcycle", new SearchContext());

        // Assert
        Assert.NotEmpty(results);
        _mockRunner.Verify(r => r.SubmitToolOutputsAsync(
            ThreadId, RunId, It.IsAny<IEnumerable<AgentToolOutput>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteSequentialSearchAsync_FailedRun_ThrowsInvalidOperationException()
    {
        // Arrange
        var failedStatus = new AgentRunStatus(RunId, AgentRunState.Failed, null);
        SetupBaseRunFlow(failedStatus);
        _mockRunner.Setup(r => r.DeleteThreadAsync(ThreadId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateOrchestrator();

        // Act & Assert
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

    private void SetupBaseRunFlow(AgentRunStatus runStatus)
    {
        _mockRunner.Setup(r => r.CreateThreadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ThreadId);
        _mockRunner.Setup(r => r.AddUserMessageAsync(ThreadId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRunner.Setup(r => r.CreateRunAsync(ThreadId, AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(runStatus);
    }
}
