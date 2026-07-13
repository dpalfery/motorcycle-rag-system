using Microsoft.Extensions.Logging;
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

namespace MotorcycleRAG.UnitTests.Agents;

public class OrchestratorToolHandlersValidationTests
{
    private readonly Mock<IFoundryAgentRunner> _runner = new(MockBehavior.Strict);
    private readonly Mock<ILogger<FoundryToolDispatcher>> _dispatcherLogger = new();
    private readonly Mock<ILogger<OrchestratorToolHandlers>> _logger = new();
    private readonly Mock<ILogger<QuestionValidationService>> _validationLogger = new();
    private readonly Mock<IAzureSearchClient> _search = new();
    private readonly Mock<ITrustedSourcesLoader> _sources = new();
    private readonly Mock<IGraphRepository> _graph = new();
    private readonly Mock<IBikeModelRepository> _bikes = new();

    private OrchestratorToolHandlers CreateHandlers(QuestionValidationState state)
    {
        var options = Options.Create(new AzureFoundryOptions
        {
            VectorSearchAgentName = "vector",
            WebSearchAgentName = "web",
            PDFSearchAgentName = "pdf",
            GraphQueryAgentName = "graph"
        });

        var subAgentHandlers = new SubAgentToolHandlers(
            _search.Object,
            _sources.Object,
            _graph.Object,
            new Mock<ILogger<SubAgentToolHandlers>>().Object);
        var subDispatcher = new FoundryToolDispatcher(_dispatcherLogger.Object);
        subAgentHandlers.RegisterOn(subDispatcher);

        return new OrchestratorToolHandlers(
            _runner.Object,
            subDispatcher,
            options,
            _logger.Object,
            new DegradedModeTracker(new Mock<ILogger<DegradedModeTracker>>().Object),
            new QuestionValidationService(_bikes.Object, _graph.Object, _validationLogger.Object),
            state);
    }

    [Fact]
    public async Task SearchTool_BeforeValidation_ReturnsGuardError()
    {
        var state = new QuestionValidationState();
        var handlers = CreateHandlers(state);

        var output = await handlers.HandleVectorSearchAsync(
            new AgentToolCall("call-1", "vector_search", "{\"query\":\"test\"}"),
            CancellationToken.None);

        Assert.Contains("validation_required", output.Output);
        _runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SearchTool_WhenClarificationRequired_ReturnsGuardError()
    {
        var state = new QuestionValidationState();
        state.Record(new QuestionValidationResult
        {
            MaySearch = false,
            ResponseType = "Clarification",
            ClarificationQuestion = "Did you mean another bike?",
            Suggestions =
            [
                new QueryClarificationSuggestion
                {
                    Label = "2026 Ducati Panigale V4",
                    Query = "What are the specs on the 2026 Ducati Panigale V4?",
                    Subject = "MotorcycleSpecs"
                }
            ]
        });
        var handlers = CreateHandlers(state);

        var output = await handlers.HandleWebSearchAsync(
            new AgentToolCall("call-2", "web_search", "{\"query\":\"test\"}"),
            CancellationToken.None);

        Assert.Contains("clarification_required", output.Output);
        Assert.Contains("Ducati Panigale V4", output.Output);
        _runner.VerifyNoOtherCalls();
    }
}
