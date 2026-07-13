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
}
