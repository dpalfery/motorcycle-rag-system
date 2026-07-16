using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Agents.Orchestration;
using MotorcycleRAG.Application.Services.QueryValidation;
using MotorcycleRAG.Application.Services.Telemetry;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Agents.Orchestration;

public class OrchestratorToolHandlersTests
{
    private readonly Mock<IFoundryAgentRunner> _runnerMock;
    private readonly FoundryToolDispatcher _dispatcher;
    private readonly Mock<IOptions<AzureFoundryOptions>> _optionsMock;
    private readonly DegradedModeTracker _tracker;
    private readonly QuestionValidationService _validationService;
    private readonly QuestionValidationState _validationState;

    public OrchestratorToolHandlersTests()
    {
        _runnerMock = new Mock<IFoundryAgentRunner>();
        _dispatcher = new FoundryToolDispatcher(NullLogger<FoundryToolDispatcher>.Instance);
        _optionsMock = new Mock<IOptions<AzureFoundryOptions>>();
        _optionsMock.Setup(o => o.Value).Returns(new AzureFoundryOptions
        {
            VectorSearchAgentName = "vector-agent",
            WebSearchAgentName = "web-agent",
            PDFSearchAgentName = "pdf-agent",
            GraphQueryAgentName = "graph-agent"
        });

        _tracker = new DegradedModeTracker(NullLogger<DegradedModeTracker>.Instance);
        
        // QuestionValidationService doesn't have an interface, we might need a mock if methods are virtual or interface exists.
        // Assuming it's mockable or we just mock it for null checks
        _validationService = new QuestionValidationService(new Mock<IBikeModelRepository>().Object, new Mock<IGraphRepository>().Object, NullLogger<QuestionValidationService>.Instance);
        _validationState = new QuestionValidationState();
        _validationState.Initialize("query", null);
    }

    [Fact]
    public void Constructor_NullDependencies_Throws()
    {
        var logger = NullLogger<OrchestratorToolHandlers>.Instance;
        Assert.Throws<ArgumentNullException>(() => new OrchestratorToolHandlers(null!, _dispatcher, _optionsMock.Object, logger, _tracker, _validationService, _validationState));
    }

    [Fact]
    public void RegisterOn_RegistersHandlers()
    {
        var logger = NullLogger<OrchestratorToolHandlers>.Instance;
        var handlers = new OrchestratorToolHandlers(_runnerMock.Object, _dispatcher, _optionsMock.Object, logger, _tracker, _validationService, _validationState);

        var dispatcher = new FoundryToolDispatcher(NullLogger<FoundryToolDispatcher>.Instance);
        handlers.RegisterOn(dispatcher);
        
        // Registration adds it to the dispatcher. Since there's no public property to check, we just ensure it doesn't throw.
    }

    [Fact]
    public async Task HandleVectorSearchAsync_WhenNotValidated_ReturnsError()
    {
        var logger = NullLogger<OrchestratorToolHandlers>.Instance;
        var state = new QuestionValidationState(); // IsValidated = false
        var handlers = new OrchestratorToolHandlers(_runnerMock.Object, _dispatcher, _optionsMock.Object, logger, _tracker, _validationService, state);

        var call = new AgentToolCall("call_123", "vector_search", "{}");
        var result = await handlers.HandleVectorSearchAsync(call, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("validation_required", result.Output);
    }

    [Fact]
    public async Task HandleValidateQuestionAsync_WithQueryInArgs_UsesProvidedQueryAndReturnsMatchingCallId()
    {
        var logger = NullLogger<OrchestratorToolHandlers>.Instance;
        var state = new QuestionValidationState();
        state.Initialize("fallback query should not be used", null);
        var handlers = new OrchestratorToolHandlers(_runnerMock.Object, _dispatcher, _optionsMock.Object, logger, _tracker, _validationService, state);

        var call = new AgentToolCall("call_abc", "validate_question", "{\"query\":\"hello there\"}");
        var result = await handlers.HandleValidateQuestionAsync(call, CancellationToken.None);

        Assert.Equal("call_abc", result.CallId);
        Assert.Contains("\"subject\":\"Unknown\"", result.Output);
        Assert.Contains("\"responseType\":\"Answer\"", result.Output);
        Assert.Contains("\"maySearch\":true", result.Output);
    }

    [Fact]
    public async Task HandleValidateQuestionAsync_WithMissingQueryInArgs_FallsBackToStateOriginalQuery()
    {
        var logger = NullLogger<OrchestratorToolHandlers>.Instance;
        var state = new QuestionValidationState();
        state.Initialize("hello there", null);
        var handlers = new OrchestratorToolHandlers(_runnerMock.Object, _dispatcher, _optionsMock.Object, logger, _tracker, _validationService, state);

        var call = new AgentToolCall("call_1", "validate_question", "{}");
        var result = await handlers.HandleValidateQuestionAsync(call, CancellationToken.None);

        Assert.Contains("\"subject\":\"Unknown\"", result.Output);
        Assert.Contains("\"responseType\":\"Answer\"", result.Output);
    }

    [Fact]
    public async Task HandleValidateQuestionAsync_WithWhitespaceOnlyQueryInArgs_FallsBackToStateOriginalQuery()
    {
        var logger = NullLogger<OrchestratorToolHandlers>.Instance;
        var state = new QuestionValidationState();
        state.Initialize("hello there", null);
        var handlers = new OrchestratorToolHandlers(_runnerMock.Object, _dispatcher, _optionsMock.Object, logger, _tracker, _validationService, state);

        var call = new AgentToolCall("call_2", "validate_question", "{\"query\":\"   \"}");
        var result = await handlers.HandleValidateQuestionAsync(call, CancellationToken.None);

        Assert.Contains("\"subject\":\"Unknown\"", result.Output);
    }

    [Fact]
    public async Task HandleValidateQuestionAsync_AfterCall_RecordsResultOnState()
    {
        var logger = NullLogger<OrchestratorToolHandlers>.Instance;
        var state = new QuestionValidationState();
        state.Initialize("hello there", null);
        var handlers = new OrchestratorToolHandlers(_runnerMock.Object, _dispatcher, _optionsMock.Object, logger, _tracker, _validationService, state);

        Assert.False(state.IsValidated);

        var call = new AgentToolCall("call_3", "validate_question", "{}");
        await handlers.HandleValidateQuestionAsync(call, CancellationToken.None);

        Assert.True(state.IsValidated);
        Assert.NotNull(state.Result);
        Assert.Equal("Unknown", state.Result!.Subject);
    }

    [Fact]
    public async Task HandleValidateQuestionAsync_WhenQueryTriggersClarification_ReturnsMaySearchFalse()
    {
        var logger = NullLogger<OrchestratorToolHandlers>.Instance;
        var state = new QuestionValidationState();
        state.Initialize("original", null);
        var handlers = new OrchestratorToolHandlers(_runnerMock.Object, _dispatcher, _optionsMock.Object, logger, _tracker, _validationService, state);

        // "trip" is classified as TripPlanning, which always short-circuits to a clarification.
        var call = new AgentToolCall("call_4", "validate_question", "{\"query\":\"plan a road trip for me\"}");
        var result = await handlers.HandleValidateQuestionAsync(call, CancellationToken.None);

        Assert.Contains("\"responseType\":\"Clarification\"", result.Output);
        Assert.Contains("\"maySearch\":false", result.Output);
        Assert.True(state.IsValidated);
        Assert.False(state.Result!.MaySearch);
    }

    [Fact]
    public async Task HandleVectorSearchAsync_MissingAgentName_ReturnsConfigError()
    {
        var logger = NullLogger<OrchestratorToolHandlers>.Instance;
        var state = new QuestionValidationState();
        state.Initialize("query", null);
        state.Record(new QuestionValidationResult { NormalizedQuery = "query", Subject = "Subject", MaySearch = true }); // IsValidated = true, MaySearch = true

        var optionsMock = new Mock<IOptions<AzureFoundryOptions>>();
        optionsMock.Setup(o => o.Value).Returns(new AzureFoundryOptions
        {
            VectorSearchAgentName = null!, // missing agent name
            WebSearchAgentName = "web-agent",
            PDFSearchAgentName = "pdf-agent",
            GraphQueryAgentName = "graph-agent"
        });

        var handlers = new OrchestratorToolHandlers(_runnerMock.Object, _dispatcher, optionsMock.Object, logger, _tracker, _validationService, state);

        var call = new AgentToolCall("call_1", "vector_search", "{}");
        var result = await handlers.HandleVectorSearchAsync(call, CancellationToken.None);

        Assert.Contains("Agent name for \\u0027vector_search\\u0027 is not configured", result.Output);
    }

    [Fact]
    public async Task HandleVectorSearchAsync_SuccessfulRun_ReturnsResultAndDeletesConversation()
    {
        var logger = NullLogger<OrchestratorToolHandlers>.Instance;
        var state = new QuestionValidationState();
        state.Initialize("query", null);
        state.Record(new QuestionValidationResult { NormalizedQuery = "query", Subject = "Subject", MaySearch = true }); // IsValidated = true, MaySearch = true

        _runnerMock.Setup(r => r.CreateConversationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("conv_1");
        _runnerMock.Setup(r => r.SendAgentMessageAsync("conv_1", "vector-agent", "{}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponseStatus("res_1", AgentRunState.Completed, null, "Agent response text"));
        _runnerMock.Setup(r => r.DeleteConversationAsync("conv_1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handlers = new OrchestratorToolHandlers(_runnerMock.Object, _dispatcher, _optionsMock.Object, logger, _tracker, _validationService, state);

        var call = new AgentToolCall("call_1", "vector_search", "{}");
        var result = await handlers.HandleVectorSearchAsync(call, CancellationToken.None);

        Assert.Contains("Agent response text", result.Output);
        _runnerMock.Verify(r => r.CreateConversationAsync(It.IsAny<CancellationToken>()), Times.Once);
        _runnerMock.Verify(r => r.SendAgentMessageAsync("conv_1", "vector-agent", "{}", It.IsAny<CancellationToken>()), Times.Once);
        _runnerMock.Verify(r => r.DeleteConversationAsync("conv_1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleVectorSearchAsync_RequiresAction_ExecutesToolCallsAndSubmitsOutputs()
    {
        var logger = NullLogger<OrchestratorToolHandlers>.Instance;
        var state = new QuestionValidationState();
        state.Initialize("query", null);
        state.Record(new QuestionValidationResult { NormalizedQuery = "query", Subject = "Subject", MaySearch = true }); // IsValidated = true, MaySearch = true

        _runnerMock.Setup(r => r.CreateConversationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("conv_2");
        _runnerMock.Setup(r => r.SendAgentMessageAsync("conv_2", "vector-agent", "{}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponseStatus("res_1", AgentRunState.RequiresAction, new[] { new AgentToolCall("subcall_1", "subtool", "{}") }, string.Empty));
        _runnerMock.Setup(r => r.SubmitToolOutputsAsync("conv_2", "vector-agent", It.IsAny<IEnumerable<AgentToolOutput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponseStatus("res_2", AgentRunState.Completed, null, "Final agent output"));

        var dispatcher = new FoundryToolDispatcher(NullLogger<FoundryToolDispatcher>.Instance);
        dispatcher.RegisterHandler("subtool", (c, ct) => Task.FromResult(new AgentToolOutput(c.CallId, "tool output")));

        var handlers = new OrchestratorToolHandlers(_runnerMock.Object, dispatcher, _optionsMock.Object, logger, _tracker, _validationService, state);

        var call = new AgentToolCall("call_1", "vector_search", "{}");
        var result = await handlers.HandleVectorSearchAsync(call, CancellationToken.None);

        Assert.Contains("Final agent output", result.Output);
        _runnerMock.Verify(r => r.SubmitToolOutputsAsync("conv_2", "vector-agent", It.Is<IEnumerable<AgentToolOutput>>(o => o.Any(x => x.CallId == "subcall_1" && x.Output == "tool output")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleVectorSearchAsync_LoopLimitsMaxRounds_ReturnsNonCompletedError()
    {
        var logger = NullLogger<OrchestratorToolHandlers>.Instance;
        var state = new QuestionValidationState();
        state.Initialize("query", null);
        state.Record(new QuestionValidationResult { NormalizedQuery = "query", Subject = "Subject", MaySearch = true }); // IsValidated = true, MaySearch = true

        _runnerMock.Setup(r => r.CreateConversationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("conv_3");
        _runnerMock.Setup(r => r.SendAgentMessageAsync("conv_3", "vector-agent", "{}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponseStatus("res_1", AgentRunState.RequiresAction, new[] { new AgentToolCall("subcall_1", "subtool", "{}") }, string.Empty));
        _runnerMock.Setup(r => r.SubmitToolOutputsAsync("conv_3", "vector-agent", It.IsAny<IEnumerable<AgentToolOutput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponseStatus("res_loop", AgentRunState.RequiresAction, new[] { new AgentToolCall("subcall_1", "subtool", "{}") }, string.Empty));

        var dispatcher = new FoundryToolDispatcher(NullLogger<FoundryToolDispatcher>.Instance);
        dispatcher.RegisterHandler("subtool", (c, ct) => Task.FromResult(new AgentToolOutput(c.CallId, "tool output")));

        var handlers = new OrchestratorToolHandlers(_runnerMock.Object, dispatcher, _optionsMock.Object, logger, _tracker, _validationService, state);

        var call = new AgentToolCall("call_1", "vector_search", "{}");
        var result = await handlers.HandleVectorSearchAsync(call, CancellationToken.None);

        Assert.Contains("Sub-agent run ended with state RequiresAction", result.Output);
        _runnerMock.Verify(r => r.SubmitToolOutputsAsync("conv_3", "vector-agent", It.IsAny<IEnumerable<AgentToolOutput>>(), It.IsAny<CancellationToken>()), Times.Exactly(8));
    }

    [Fact]
    public async Task HandleVectorSearchAsync_RunFailedState_TracksResultAndReturnsError()
    {
        var logger = NullLogger<OrchestratorToolHandlers>.Instance;
        var state = new QuestionValidationState();
        state.Initialize("query", null);
        state.Record(new QuestionValidationResult { NormalizedQuery = "query", Subject = "Subject", MaySearch = true }); // IsValidated = true, MaySearch = true

        _runnerMock.Setup(r => r.CreateConversationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("conv_4");
        _runnerMock.Setup(r => r.SendAgentMessageAsync("conv_4", "vector-agent", "{}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponseStatus("res_fail", AgentRunState.Failed, null, string.Empty));

        var handlers = new OrchestratorToolHandlers(_runnerMock.Object, _dispatcher, _optionsMock.Object, logger, _tracker, _validationService, state);

        var call = new AgentToolCall("call_1", "vector_search", "{}");
        var result = await handlers.HandleVectorSearchAsync(call, CancellationToken.None);

        Assert.Contains("Sub-agent run ended with state Failed", result.Output);
    }

    [Fact]
    public async Task HandleVectorSearchAsync_RunnerThrowsExceptionDuringDelete_StillSucceeds()
    {
        var logger = NullLogger<OrchestratorToolHandlers>.Instance;
        var state = new QuestionValidationState();
        state.Initialize("query", null);
        state.Record(new QuestionValidationResult { NormalizedQuery = "query", Subject = "Subject", MaySearch = true }); // IsValidated = true, MaySearch = true

        _runnerMock.Setup(r => r.CreateConversationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("conv_5");
        _runnerMock.Setup(r => r.SendAgentMessageAsync("conv_5", "vector-agent", "{}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponseStatus("res_ok", AgentRunState.Completed, null, "Hello Response"));
        _runnerMock.Setup(r => r.DeleteConversationAsync("conv_5", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Delete failed"));

        var handlers = new OrchestratorToolHandlers(_runnerMock.Object, _dispatcher, _optionsMock.Object, logger, _tracker, _validationService, state);

        var call = new AgentToolCall("call_1", "vector_search", "{}");
        var result = await handlers.HandleVectorSearchAsync(call, CancellationToken.None);

        Assert.Contains("Hello Response", result.Output);
    }
}
