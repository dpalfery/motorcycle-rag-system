using Azure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.AgentProvisioning.Azure;
using OpenAI.Responses;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace MotorcycleRAG.UnitTests.Services.AgentProvisioning;

public sealed class AgentProvisioningServiceTests
{
    [Fact]
    public async Task ProvisionAllAgentsAsync_WhenAllOrchestratorCandidatesSucceed_UsesTheLastSuccessfulVersion()
    {
        // Arrange
        var operations = new RecordingAgentAdminOperations();
        var sut = CreateService(operations, ["primary", "fallback"]);

        // Act
        var result = await sut.ProvisionAllAgentsAsync();

        // Assert
        result.Orchestrator.Should().Be(new ProvisionedAgentReference(AgentDefinitions.OrchestratorAgentName, "fallback-version"));
        result.VectorSearch.Name.Should().Be(AgentDefinitions.VectorSearchAgentName);
        result.WebSearch.Name.Should().Be(AgentDefinitions.WebSearchAgentName);
        result.PDFSearch.Name.Should().Be(AgentDefinitions.PDFSearchAgentName);
        result.GraphQuery.Name.Should().Be(AgentDefinitions.GraphQueryAgentName);
        operations.CreatedModels.Take(2).Should().Equal("primary", "fallback");
    }

    [Fact]
    public async Task ProvisionAllAgentsAsync_WhenCandidateThrows_LogsAndContinuesToRemainingCandidates()
    {
        // Arrange
        var operations = new RecordingAgentAdminOperations((name, model, _) =>
            name == AgentDefinitions.OrchestratorAgentName && model == "primary"
                ? new InvalidOperationException("candidate failed")
                : null);
        var sut = CreateService(operations, ["primary", "fallback"]);

        // Act
        var result = await sut.ProvisionAllAgentsAsync();

        // Assert
        result.Orchestrator.Version.Should().Be("fallback-version");
        operations.CreatedModels.Take(2).Should().Equal("primary", "fallback");
    }

    [Fact]
    public async Task ProvisionAllAgentsAsync_WhenNoCandidateSucceeds_ThrowsTheOriginalNoVersionException()
    {
        // Arrange
        var operations = new RecordingAgentAdminOperations((name, _, _) =>
            name == AgentDefinitions.OrchestratorAgentName
                ? new InvalidOperationException("candidate failed")
                : null);
        var sut = CreateService(operations, ["primary", "fallback"]);

        // Act
        var act = () => sut.ProvisionAllAgentsAsync();

        // Assert
        var exception = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("Failed to create any orchestrator agent versions.");
        exception.InnerException.Should().BeNull();
        operations.CreatedModels.Should().Equal("primary", "fallback");
    }

    [Fact]
    public async Task ProvisionAllAgentsAsync_WhenNoCandidatesAreConfigured_ThrowsTheOriginalNoVersionException()
    {
        // Arrange
        var operations = new RecordingAgentAdminOperations();
        var sut = CreateService(operations, []);

        // Act
        var act = () => sut.ProvisionAllAgentsAsync();

        // Assert
        var exception = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("Failed to create any orchestrator agent versions.");
        operations.GetAgentNamesCalls.Should().Be(0);
        operations.CreatedModels.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAgentVersionWithFallbackAsync_WhenFirstCandidateSucceeds_ReturnsWithoutTryingLaterCandidates()
    {
        // Arrange
        var operations = new RecordingAgentAdminOperations();
        var sut = CreateService(operations, ["unused"]);

        // Act
        var result = await sut.CreateAgentVersionWithFallbackAsync(
            AgentDefinitions.OrchestratorAgentName,
            ["primary", "fallback"],
            AgentDefinitions.OrchestratorSystemPrompt,
            AgentDefinitions.OrchestratorTools,
            CancellationToken.None);

        // Assert
        result.Version.Should().Be("primary-version");
        operations.CreatedModels.Should().Equal("primary");
    }

    [Fact]
    public async Task CreateAgentVersionWithFallbackAsync_WhenModelIsRejected_TriesTheNextCandidate()
    {
        // Arrange
        var operations = new RecordingAgentAdminOperations((_, model, _) =>
            model == "primary" ? new RequestFailedException(400, "model deployment is not supported") : null);
        var sut = CreateService(operations, ["unused"]);

        // Act
        var result = await sut.CreateAgentVersionWithFallbackAsync(
            AgentDefinitions.OrchestratorAgentName,
            ["primary", "fallback"],
            AgentDefinitions.OrchestratorSystemPrompt,
            AgentDefinitions.OrchestratorTools,
            CancellationToken.None);

        // Assert
        result.Version.Should().Be("fallback-version");
        operations.CreatedModels.Should().Equal("primary", "fallback");
    }

    [Fact]
    public async Task CreateAgentVersionWithFallbackAsync_WhenFailureIsNotRetryable_PropagatesWithoutTryingLaterCandidates()
    {
        // Arrange
        var failure = new RequestFailedException(500, "service unavailable");
        var operations = new RecordingAgentAdminOperations((_, _, _) => failure);
        var sut = CreateService(operations, ["unused"]);

        // Act
        var act = () => sut.CreateAgentVersionWithFallbackAsync(
            AgentDefinitions.OrchestratorAgentName,
            ["primary", "fallback"],
            AgentDefinitions.OrchestratorSystemPrompt,
            AgentDefinitions.OrchestratorTools,
            CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<RequestFailedException>()).Which.Should().BeSameAs(failure);
        operations.CreatedModels.Should().Equal("primary");
    }

    [Fact]
    public async Task CreateAgentVersionWithFallbackAsync_WhenEveryCandidateIsRejected_ThrowsWithTheLastRejection()
    {
        // Arrange
        var operations = new RecordingAgentAdminOperations((_, _, _) =>
            new RequestFailedException(404, "model deployment not found"));
        var sut = CreateService(operations, ["unused"]);

        // Act
        var act = () => sut.CreateAgentVersionWithFallbackAsync(
            AgentDefinitions.OrchestratorAgentName,
            ["primary", "fallback"],
            AgentDefinitions.OrchestratorSystemPrompt,
            AgentDefinitions.OrchestratorTools,
            CancellationToken.None);

        // Assert
        var exception = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Contain("all configured model deployment candidates");
        exception.InnerException.Should().BeOfType<RequestFailedException>();
        operations.CreatedModels.Should().Equal("primary", "fallback");
    }

    [Fact]
    public async Task CreateAgentVersionWithFallbackAsync_WhenNoCandidatesAreConfigured_ThrowsBeforeCallingOperations()
    {
        // Arrange
        var operations = new RecordingAgentAdminOperations();
        var sut = CreateService(operations, ["unused"]);

        // Act
        var act = () => sut.CreateAgentVersionWithFallbackAsync(
            AgentDefinitions.OrchestratorAgentName,
            [],
            AgentDefinitions.OrchestratorSystemPrompt,
            AgentDefinitions.OrchestratorTools,
            CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain("No model deployment candidates configured");
        operations.GetAgentNamesCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(400, "model deployment is not supported", true)]
    [InlineData(404, "deployment not found", true)]
    [InlineData(500, "model deployment is not supported", false)]
    [InlineData(400, "request is malformed", false)]
    public void ShouldTryNextModel_WhenRequestFailedExceptionMatchesModelRejection_ClassifiesCorrectly(
        int status,
        string message,
        bool expected)
    {
        // Arrange
        var exception = new RequestFailedException(status, message);

        // Act
        var result = AgentProvisioningService.ShouldTryNextModel(exception);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(400, "invalid model", true)]
    [InlineData(404, "deployment not found", true)]
    [InlineData(500, "invalid model", false)]
    [InlineData(400, "access denied", false)]
    public void ShouldTryNextModel_WhenClientResultExceptionMatchesModelRejection_ClassifiesCorrectly(
        int status,
        string message,
        bool expected)
    {
        // Arrange
        var exception = CreateClientResultException(status, message);

        // Act
        var result = AgentProvisioningService.ShouldTryNextModel(exception);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void ShouldTryNextModel_WhenExceptionIsNotAnAzureClientFailure_ReturnsFalse()
    {
        // Arrange
        var exception = new InvalidOperationException("model deployment is not supported");

        // Act
        var result = AgentProvisioningService.ShouldTryNextModel(exception);

        // Assert
        result.Should().BeFalse();
    }

    private static AgentProvisioningService CreateService(
        RecordingAgentAdminOperations operations,
        IReadOnlyList<string> candidates) => new(
        operations,
        new AgentProvisioningModelOptions(candidates, "subagent-model"),
        NullLogger<AgentProvisioningService>.Instance);

    private static ClientResultException CreateClientResultException(int status, string message)
    {
        var response = new Mock<PipelineResponse>();
        response.SetupGet(value => value.Status).Returns(status);
        return new ClientResultException(message, response.Object, innerException: null);
    }

    private sealed class RecordingAgentAdminOperations(
        Func<string, string, int, Exception?>? createFailure = null) : IAgentAdminOperations
    {
        private int _createCalls;

        public int GetAgentNamesCalls { get; private set; }
        public List<string> CreatedModels { get; } = [];

        public Task<IReadOnlyList<string>> GetAgentNamesAsync(CancellationToken ct = default)
        {
            GetAgentNamesCalls++;
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<ProvisionedAgentReference> CreateAgentVersionAsync(
            string name,
            string model,
            string instructions,
            ResponseTool[] tools,
            CancellationToken ct = default)
        {
            _createCalls++;
            CreatedModels.Add(model);

            var failure = createFailure?.Invoke(name, model, _createCalls);
            return failure is null
                ? Task.FromResult(new ProvisionedAgentReference(name, $"{model}-version"))
                : Task.FromException<ProvisionedAgentReference>(failure);
        }
    }
}
