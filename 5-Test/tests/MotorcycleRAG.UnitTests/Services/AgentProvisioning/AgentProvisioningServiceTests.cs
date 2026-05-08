using Azure.AI.Agents.Persistent;
using Azure;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.AgentProvisioning.Azure;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.AgentProvisioning;

/// <summary>
/// Unit tests for <see cref="AgentProvisioningService"/>.
/// Uses a mock <see cref="IAgentAdminOperations"/> to isolate from Azure SDK.
/// </summary>
public class AgentProvisioningServiceTests {
    private readonly Mock<IAgentAdminOperations> _mockOps;
    private readonly Mock<ILogger<AgentProvisioningService>> _mockLogger;
    private readonly AgentProvisioningService _service;

    public AgentProvisioningServiceTests() {
        _mockOps = new Mock<IAgentAdminOperations>();
        _mockLogger = new Mock<ILogger<AgentProvisioningService>>();

        // Default: no existing agents
        _mockOps.Setup(o => o.GetAgentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string Id, string Name)>().AsReadOnly());

        _service = new AgentProvisioningService(_mockOps.Object, _mockLogger.Object);
    }

    // -------------------------------------------------------------------------
    // Create when agent does not exist
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProvisionAllAgentsAsync_ShouldCreateAllAgents_WhenNoneExist() {
        // Arrange — mock returns unique IDs for each create call
        _mockOps.SetupSequence(o => o.CreateAgentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ToolDefinition[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("orchestrator-id")
            .ReturnsAsync("vectorsearch-id")
            .ReturnsAsync("websearch-id")
            .ReturnsAsync("pdfsearch-id");

        // Act
        var result = await _service.ProvisionAllAgentsAsync();

        // Assert — all four agents created
        Assert.Equal("orchestrator-id", result.OrchestratorAgentId);
        Assert.Equal("vectorsearch-id", result.VectorSearchAgentId);
        Assert.Equal("websearch-id", result.WebSearchAgentId);
        Assert.Equal("pdfsearch-id", result.PDFSearchAgentId);

        _mockOps.Verify(o => o.CreateAgentAsync(
            AgentDefinitions.OrchestratorAgentName,
            AgentDefinitions.Qwen36DeploymentName,
            AgentDefinitions.OrchestratorSystemPrompt,
            It.IsAny<ToolDefinition[]>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockOps.Verify(o => o.CreateAgentAsync(
            AgentDefinitions.VectorSearchAgentName,
            AgentDefinitions.SubAgentModel,
            AgentDefinitions.VectorSearchSystemPrompt,
            It.IsAny<ToolDefinition[]>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockOps.Verify(o => o.CreateAgentAsync(
            AgentDefinitions.WebSearchAgentName,
            AgentDefinitions.SubAgentModel,
            AgentDefinitions.WebSearchSystemPrompt,
            It.IsAny<ToolDefinition[]>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockOps.Verify(o => o.CreateAgentAsync(
            AgentDefinitions.PDFSearchAgentName,
            AgentDefinitions.SubAgentModel,
            AgentDefinitions.PDFSearchSystemPrompt,
            It.IsAny<ToolDefinition[]>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockOps.Verify(o => o.UpdateAgentAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<ToolDefinition[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -------------------------------------------------------------------------
    // Update (upsert) when agent already exists
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProvisionAllAgentsAsync_ShouldUpdateOrchestratorAgent_WhenItAlreadyExists() {
        // Arrange — orchestrator agent already exists
        _mockOps.Setup(o => o.GetAgentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string Id, string Name)>
            {
                ("existing-orchestrator-id", AgentDefinitions.OrchestratorAgentName)
            }.AsReadOnly());

        _mockOps.Setup(o => o.UpdateAgentAsync(
                "existing-orchestrator-id",
                AgentDefinitions.OrchestratorAgentName,
                AgentDefinitions.Qwen36DeploymentName,
                AgentDefinitions.OrchestratorSystemPrompt,
                It.IsAny<ToolDefinition[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("existing-orchestrator-id");

        _mockOps.SetupSequence(o => o.CreateAgentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ToolDefinition[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("vectorsearch-id")
            .ReturnsAsync("websearch-id")
            .ReturnsAsync("pdfsearch-id");

        // Act
        var result = await _service.ProvisionAllAgentsAsync();

        // Assert
        Assert.Equal("existing-orchestrator-id", result.OrchestratorAgentId);

        _mockOps.Verify(o => o.UpdateAgentAsync(
            "existing-orchestrator-id",
            AgentDefinitions.OrchestratorAgentName,
            AgentDefinitions.Qwen36DeploymentName,
            AgentDefinitions.OrchestratorSystemPrompt,
            It.IsAny<ToolDefinition[]>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Other three agents are new — should be created
        _mockOps.Verify(o => o.CreateAgentAsync(
            AgentDefinitions.OrchestratorAgentName,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ToolDefinition[]>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Returns correct IDs
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProvisionAllAgentsAsync_ShouldReturnAgentIdFromCreateAsync() {
        // Arrange
        _mockOps.SetupSequence(o => o.CreateAgentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ToolDefinition[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("orch-001")
            .ReturnsAsync("vec-001")
            .ReturnsAsync("web-001")
            .ReturnsAsync("pdf-001");

        // Act
        var result = await _service.ProvisionAllAgentsAsync();

        // Assert
        Assert.Equal("orch-001", result.OrchestratorAgentId);
        Assert.Equal("vec-001", result.VectorSearchAgentId);
        Assert.Equal("web-001", result.WebSearchAgentId);
        Assert.Equal("pdf-001", result.PDFSearchAgentId);
    }

    [Fact]
    public async Task ProvisionAllAgentsAsync_ShouldReturnAgentIdFromUpdateAsync() {
        // Arrange — all four agents already exist
        _mockOps.Setup(o => o.GetAgentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string Id, string Name)>
            {
                ("orch-existing", AgentDefinitions.OrchestratorAgentName),
                ("vec-existing",  AgentDefinitions.VectorSearchAgentName),
                ("web-existing",  AgentDefinitions.WebSearchAgentName),
                ("pdf-existing",  AgentDefinitions.PDFSearchAgentName)
            }.AsReadOnly());

        _mockOps.Setup(o => o.UpdateAgentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<ToolDefinition[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string agentId, string _, string __, string ___, ToolDefinition[] ____, CancellationToken _____) => agentId);

        // Act
        var result = await _service.ProvisionAllAgentsAsync();

        // Assert — IDs returned from UpdateAgentAsync
        Assert.Equal("orch-existing", result.OrchestratorAgentId);
        Assert.Equal("vec-existing", result.VectorSearchAgentId);
        Assert.Equal("web-existing", result.WebSearchAgentId);
        Assert.Equal("pdf-existing", result.PDFSearchAgentId);

        _mockOps.Verify(o => o.CreateAgentAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<ToolDefinition[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -------------------------------------------------------------------------
    // Guard: missing system prompt
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProvisionAllAgentsAsync_ShouldFallback_WhenPreferredOrchestratorModelsAreRejected() {
        _mockOps.Setup(o => o.CreateAgentAsync(
                AgentDefinitions.OrchestratorAgentName,
                AgentDefinitions.Qwen36DeploymentName,
                AgentDefinitions.OrchestratorSystemPrompt,
                It.IsAny<ToolDefinition[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(400, "Model deployment is not supported by Foundry Agents"));

        _mockOps.Setup(o => o.CreateAgentAsync(
                AgentDefinitions.OrchestratorAgentName,
                AgentDefinitions.Qwen35DeploymentName,
                AgentDefinitions.OrchestratorSystemPrompt,
                It.IsAny<ToolDefinition[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(400, "Model deployment is not supported by Foundry Agents"));

        _mockOps.Setup(o => o.CreateAgentAsync(
                AgentDefinitions.OrchestratorAgentName,
                AgentDefinitions.OrchestratorFallbackModel,
                AgentDefinitions.OrchestratorSystemPrompt,
                It.IsAny<ToolDefinition[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("orchestrator-id");

        _mockOps.Setup(o => o.CreateAgentAsync(
                It.Is<string>(name => name != AgentDefinitions.OrchestratorAgentName),
                AgentDefinitions.SubAgentModel,
                It.IsAny<string>(),
                It.IsAny<ToolDefinition[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, string _, string __, ToolDefinition[] ___, CancellationToken ____) => $"{name}-id");

        var result = await _service.ProvisionAllAgentsAsync();

        Assert.Equal("orchestrator-id", result.OrchestratorAgentId);

        _mockOps.Verify(o => o.CreateAgentAsync(
            AgentDefinitions.OrchestratorAgentName,
            AgentDefinitions.OrchestratorFallbackModel,
            AgentDefinitions.OrchestratorSystemPrompt,
            It.IsAny<ToolDefinition[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void FromEnvironment_ShouldUseConfiguredModelDeployments_WhenProvided() {
        var values = new Dictionary<string, string?>
        {
            ["ORCHESTRATOR_MODEL_DEPLOYMENTS"] = "qwen-a, qwen-b; gpt-fallback",
            ["SUBAGENT_MODEL_DEPLOYMENT"] = "gpt-mini"
        };

        var options = AgentProvisioningModelOptions.FromEnvironment(key => values.GetValueOrDefault(key));

        Assert.Equal(new[] { "qwen-a", "qwen-b", "gpt-fallback" }, options.OrchestratorModelCandidates);
        Assert.Equal("gpt-mini", options.SubAgentModel);
    }

    [Fact]
    public async Task ProvisionAllAgentsAsync_ShouldThrow_WhenOrchestratorSystemPromptIsEmpty() {
        Assert.False(string.IsNullOrWhiteSpace(AgentDefinitions.OrchestratorSystemPrompt),
            "OrchestratorSystemPrompt must not be empty — production guard depends on it");
        Assert.False(string.IsNullOrWhiteSpace(AgentDefinitions.VectorSearchSystemPrompt),
            "VectorSearchSystemPrompt must not be empty");
        Assert.False(string.IsNullOrWhiteSpace(AgentDefinitions.WebSearchSystemPrompt),
            "WebSearchSystemPrompt must not be empty");
        Assert.False(string.IsNullOrWhiteSpace(AgentDefinitions.PDFSearchSystemPrompt),
            "PDFSearchSystemPrompt must not be empty");

        var stub = new EmptyPromptStubOps();
        var svc = new AgentProvisioningService(
            stub,
            _mockLogger.Object,
            orchestratorSystemPrompt: string.Empty);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ProvisionAllAgentsAsync());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Stub that simulates an empty system prompt to exercise the guard.</summary>
    private sealed class EmptyPromptStubOps : IAgentAdminOperations {
        public Task<IReadOnlyList<(string Id, string Name)>> GetAgentsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(string Id, string Name)>>(
                new List<(string Id, string Name)>().AsReadOnly());

        public Task<string> CreateAgentAsync(string name, string model, string instructions,
            ToolDefinition[] tools, CancellationToken ct = default) {
            if (string.IsNullOrWhiteSpace(instructions))
                throw new InvalidOperationException($"System prompt for agent '{name}' is missing or empty");

            return Task.FromResult("stub-id");
        }

        public Task<string> UpdateAgentAsync(string agentId, string name, string model,
            string instructions, ToolDefinition[] tools, CancellationToken ct = default)
            => Task.FromResult(agentId);
    }
}
