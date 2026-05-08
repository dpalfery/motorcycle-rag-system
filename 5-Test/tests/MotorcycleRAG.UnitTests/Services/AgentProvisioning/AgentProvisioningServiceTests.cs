using Azure;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.AgentProvisioning.Azure;
using OpenAI.Responses;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.AgentProvisioning;

/// <summary>
/// Unit tests for <see cref="AgentProvisioningService"/>.
/// Uses a mock <see cref="IAgentAdminOperations"/> to isolate from Azure SDK.
/// </summary>
public class AgentProvisioningServiceTests
{
    private readonly Mock<IAgentAdminOperations> _mockOps;
    private readonly Mock<ILogger<AgentProvisioningService>> _mockLogger;
    private readonly AgentProvisioningService _service;

    public AgentProvisioningServiceTests()
    {
        _mockOps = new Mock<IAgentAdminOperations>();
        _mockLogger = new Mock<ILogger<AgentProvisioningService>>();

        _mockOps.Setup(o => o.GetAgentNamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        _service = new AgentProvisioningService(_mockOps.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ProvisionAllAgentsAsync_ShouldCreateVersionsForAllAgents()
    {
        _mockOps.Setup(o => o.CreateAgentVersionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ResponseTool[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, string _, string __, ResponseTool[] ___, CancellationToken ____) =>
                new ProvisionedAgentReference(name, $"{name}-v1"));

        var result = await _service.ProvisionAllAgentsAsync();

        Assert.Equal(AgentDefinitions.OrchestratorAgentName, result.Orchestrator.Name);
        Assert.Equal(AgentDefinitions.VectorSearchAgentName, result.VectorSearch.Name);
        Assert.Equal(AgentDefinitions.WebSearchAgentName, result.WebSearch.Name);
        Assert.Equal(AgentDefinitions.PDFSearchAgentName, result.PDFSearch.Name);

        _mockOps.Verify(o => o.CreateAgentVersionAsync(
            AgentDefinitions.OrchestratorAgentName,
            AgentDefinitions.Qwen36DeploymentName,
            AgentDefinitions.OrchestratorSystemPrompt,
            It.IsAny<ResponseTool[]>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockOps.Verify(o => o.CreateAgentVersionAsync(
            AgentDefinitions.VectorSearchAgentName,
            AgentDefinitions.SubAgentModel,
            AgentDefinitions.VectorSearchSystemPrompt,
            It.IsAny<ResponseTool[]>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockOps.Verify(o => o.CreateAgentVersionAsync(
            AgentDefinitions.WebSearchAgentName,
            AgentDefinitions.SubAgentModel,
            AgentDefinitions.WebSearchSystemPrompt,
            It.IsAny<ResponseTool[]>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockOps.Verify(o => o.CreateAgentVersionAsync(
            AgentDefinitions.PDFSearchAgentName,
            AgentDefinitions.SubAgentModel,
            AgentDefinitions.PDFSearchSystemPrompt,
            It.IsAny<ResponseTool[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProvisionAllAgentsAsync_ShouldCreateNewVersion_WhenAgentAlreadyExists()
    {
        _mockOps.Setup(o => o.GetAgentNamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([AgentDefinitions.OrchestratorAgentName]);

        _mockOps.Setup(o => o.CreateAgentVersionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ResponseTool[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, string _, string __, ResponseTool[] ___, CancellationToken ____) =>
                new ProvisionedAgentReference(name, "2"));

        var result = await _service.ProvisionAllAgentsAsync();

        Assert.Equal(AgentDefinitions.OrchestratorAgentName, result.Orchestrator.Name);
        Assert.Equal("2", result.Orchestrator.Version);

        _mockOps.Verify(o => o.CreateAgentVersionAsync(
            AgentDefinitions.OrchestratorAgentName,
            AgentDefinitions.Qwen36DeploymentName,
            AgentDefinitions.OrchestratorSystemPrompt,
            It.IsAny<ResponseTool[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProvisionAllAgentsAsync_ShouldFallback_WhenPreferredOrchestratorModelsAreRejected()
    {
        _mockOps.Setup(o => o.CreateAgentVersionAsync(
                AgentDefinitions.OrchestratorAgentName,
                AgentDefinitions.Qwen36DeploymentName,
                AgentDefinitions.OrchestratorSystemPrompt,
                It.IsAny<ResponseTool[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(400, "Model deployment is not supported by Foundry Agents"));

        _mockOps.Setup(o => o.CreateAgentVersionAsync(
                AgentDefinitions.OrchestratorAgentName,
                AgentDefinitions.Qwen35DeploymentName,
                AgentDefinitions.OrchestratorSystemPrompt,
                It.IsAny<ResponseTool[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(400, "Model deployment is not supported by Foundry Agents"));

        _mockOps.Setup(o => o.CreateAgentVersionAsync(
                AgentDefinitions.OrchestratorAgentName,
                AgentDefinitions.OrchestratorFallbackModel,
                AgentDefinitions.OrchestratorSystemPrompt,
                It.IsAny<ResponseTool[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProvisionedAgentReference(AgentDefinitions.OrchestratorAgentName, "fallback-v1"));

        _mockOps.Setup(o => o.CreateAgentVersionAsync(
                It.Is<string>(name => name != AgentDefinitions.OrchestratorAgentName),
                AgentDefinitions.SubAgentModel,
                It.IsAny<string>(),
                It.IsAny<ResponseTool[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, string _, string __, ResponseTool[] ___, CancellationToken ____) =>
                new ProvisionedAgentReference(name, "sub-v1"));

        var result = await _service.ProvisionAllAgentsAsync();

        Assert.Equal("fallback-v1", result.Orchestrator.Version);

        _mockOps.Verify(o => o.CreateAgentVersionAsync(
            AgentDefinitions.OrchestratorAgentName,
            AgentDefinitions.OrchestratorFallbackModel,
            AgentDefinitions.OrchestratorSystemPrompt,
            It.IsAny<ResponseTool[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void FromEnvironment_ShouldUseConfiguredModelDeployments_WhenProvided()
    {
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
    public async Task ProvisionAllAgentsAsync_ShouldThrow_WhenOrchestratorSystemPromptIsEmpty()
    {
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

    private sealed class EmptyPromptStubOps : IAgentAdminOperations
    {
        public Task<IReadOnlyList<string>> GetAgentNamesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<ProvisionedAgentReference> CreateAgentVersionAsync(
            string name,
            string model,
            string instructions,
            ResponseTool[] tools,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(instructions))
                throw new InvalidOperationException($"System prompt for agent '{name}' is missing or empty");

            return Task.FromResult(new ProvisionedAgentReference(name, "stub-version"));
        }
    }
}
