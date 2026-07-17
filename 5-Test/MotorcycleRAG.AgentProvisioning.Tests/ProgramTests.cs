using Azure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.AgentProvisioning;
using MotorcycleRAG.AgentProvisioning.Azure;
using OpenAI.Responses;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;

namespace MotorcycleRAG.UnitTests.AgentProvisioning;

public sealed class ProgramTests
{
    [Fact]
    public async Task Main_WhenFoundryEndpointIsMissing_ReturnsOne()
    {
        // Arrange
        var originalEndpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_ENDPOINT");
        Environment.SetEnvironmentVariable("AZURE_FOUNDRY_ENDPOINT", null);

        try
        {
            // Act
            var exitCode = await Program.Main([]);

            // Assert
            exitCode.Should().Be(1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZURE_FOUNDRY_ENDPOINT", originalEndpoint);
        }
    }

    [Fact]
    public async Task RunAsync_WhenFoundryEndpointIsMissing_ReturnsOneWithoutCreatingOperations()
    {
        // Arrange
        var operationsCreated = false;

        // Act
        var exitCode = await Program.RunAsync(
            getEnvironmentVariable: _ => null,
            createAdminOperations: _ =>
            {
                operationsCreated = true;
                return new RecordingAgentAdminOperations();
            },
            writeOutput: _ => throw new Xunit.Sdk.XunitException("A configuration failure must not write JSON output."));

        // Assert
        exitCode.Should().Be(1);
        operationsCreated.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WhenProvisioningSucceeds_WritesAllFiveAgentReferencesAsJsonAndReturnsZero()
    {
        // Arrange
        var output = new List<string>();
        var operations = new RecordingAgentAdminOperations();

        // Act
        var exitCode = await Program.RunAsync(
            getEnvironmentVariable: key => key == "AZURE_FOUNDRY_ENDPOINT" ? "https://foundry.example.test" : null,
            createAdminOperations: _ => operations,
            writeOutput: output.Add);

        // Assert
        exitCode.Should().Be(0);
        output.Should().ContainSingle();
        using var json = JsonDocument.Parse(output[0]);
        json.RootElement.GetProperty("orchestratorAgentName").GetString().Should().Be(AgentDefinitions.OrchestratorAgentName);
        json.RootElement.GetProperty("orchestratorAgentVersion").GetString().Should().Be("version-orchestrator");
        json.RootElement.GetProperty("vectorSearchAgentName").GetString().Should().Be(AgentDefinitions.VectorSearchAgentName);
        json.RootElement.GetProperty("vectorSearchAgentVersion").GetString().Should().Be("version-vector");
        json.RootElement.GetProperty("webSearchAgentName").GetString().Should().Be(AgentDefinitions.WebSearchAgentName);
        json.RootElement.GetProperty("webSearchAgentVersion").GetString().Should().Be("version-web");
        json.RootElement.GetProperty("pdfSearchAgentName").GetString().Should().Be(AgentDefinitions.PDFSearchAgentName);
        json.RootElement.GetProperty("pdfSearchAgentVersion").GetString().Should().Be("version-pdf");
        json.RootElement.GetProperty("graphQueryAgentName").GetString().Should().Be(AgentDefinitions.GraphQueryAgentName);
        json.RootElement.GetProperty("graphQueryAgentVersion").GetString().Should().Be("version-graph");
    }

    [Fact]
    public async Task RunAsync_WhenProvisioningFails_ReturnsOneAndLogsFailureWithoutWritingJson()
    {
        // Arrange
        var output = new List<string>();
        var operations = new RecordingAgentAdminOperations(getNamesFailure: _ => new InvalidOperationException("provisioning exploded"));

        // Act
        var exitCode = await Program.RunAsync(
            getEnvironmentVariable: key => key == "AZURE_FOUNDRY_ENDPOINT" ? "https://foundry.example.test" : null,
            createAdminOperations: _ => operations,
            writeOutput: output.Add);

        // Assert
        exitCode.Should().Be(1);
        output.Should().BeEmpty();
    }

    [Fact]
    public async Task ProvisionWithPermissionRetryAsync_WhenPermissionPropagates_RetriesAtCurrentDelayThenSucceeds()
    {
        // Arrange
        var remainingPermissionFailures = 1;
        var operations = new RecordingAgentAdminOperations(createFailure: (name, _, _) =>
            name == AgentDefinitions.VectorSearchAgentName && remainingPermissionFailures-- > 0
                ? new RequestFailedException(401, "permission denied", "PermissionDenied", null)
                : null);
        var service = CreateService(operations);
        var delays = new List<TimeSpan>();

        // Act
        var result = await Program.ProvisionWithPermissionRetryAsync(
            service,
            NullLogger.Instance,
            delay =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        // Assert
        result.GraphQuery.Name.Should().Be(AgentDefinitions.GraphQueryAgentName);
        operations.VectorSearchCreateCalls.Should().Be(2);
        delays.Should().Equal(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ProvisionWithPermissionRetryAsync_WhenPermissionNeverPropagates_MakesSixAttemptsAndPropagatesFinalFailure()
    {
        // Arrange
        var operations = new RecordingAgentAdminOperations(createFailure: (name, _, _) =>
            name == AgentDefinitions.VectorSearchAgentName
                ? new RequestFailedException(401, "permission denied", "PermissionDenied", null)
                : null);
        var service = CreateService(operations);
        var delays = new List<TimeSpan>();

        // Act
        var act = () => Program.ProvisionWithPermissionRetryAsync(
            service,
            NullLogger.Instance,
            delay =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        // Assert
        (await act.Should().ThrowAsync<RequestFailedException>()).Which.Status.Should().Be(401);
        operations.VectorSearchCreateCalls.Should().Be(6);
        delays.Should().HaveCount(5).And.OnlyContain(delay => delay == TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ProvisionWithPermissionRetryAsync_WhenFailureIsNotPermissionPropagation_DoesNotRetryAndPropagatesFailure()
    {
        // Arrange
        var failure = new InvalidOperationException("model service unavailable");
        var operations = new RecordingAgentAdminOperations(createFailure: (name, _, _) =>
            name == AgentDefinitions.VectorSearchAgentName ? failure : null);
        var service = CreateService(operations);
        var delays = new List<TimeSpan>();

        // Act
        var act = () => Program.ProvisionWithPermissionRetryAsync(
            service,
            NullLogger.Instance,
            delay =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        // Assert
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        operations.VectorSearchCreateCalls.Should().Be(1);
        delays.Should().BeEmpty();
    }

    [Theory]
    [InlineData(401, "PermissionDenied", true)]
    [InlineData(401, "permissiondenied", true)]
    [InlineData(403, "PermissionDenied", false)]
    [InlineData(401, "Unauthorized", false)]
    public void IsPermissionPropagationFailure_WhenRequestFailedExceptionMatchesExpectedStatusAndCode_ClassifiesCorrectly(
        int status,
        string errorCode,
        bool expected)
    {
        // Arrange
        var exception = new RequestFailedException(status, "request failed", errorCode, null);

        // Act
        var result = Program.IsPermissionPropagationFailure(exception);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(401, "PermissionDenied", true)]
    [InlineData(401, "permissiondenied", true)]
    [InlineData(403, "PermissionDenied", false)]
    [InlineData(401, "Unauthorized", false)]
    public void IsPermissionPropagationFailure_WhenClientResultExceptionMatchesExpectedStatusAndMessage_ClassifiesCorrectly(
        int status,
        string message,
        bool expected)
    {
        // Arrange
        var exception = CreateClientResultException(status, message);

        // Act
        var result = Program.IsPermissionPropagationFailure(exception);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void IsPermissionPropagationFailure_WhenExceptionIsNotAClientFailure_ReturnsFalse()
    {
        // Arrange
        var exception = new InvalidOperationException("PermissionDenied");

        // Act
        var result = Program.IsPermissionPropagationFailure(exception);

        // Assert
        result.Should().BeFalse();
    }

    private static AgentProvisioningService CreateService(RecordingAgentAdminOperations operations) => new(
        operations,
        AgentProvisioningModelOptions.Default,
        NullLogger<AgentProvisioningService>.Instance);

    private static ClientResultException CreateClientResultException(int status, string message)
    {
        var response = new Mock<PipelineResponse>();
        response.SetupGet(value => value.Status).Returns(status);
        return new ClientResultException(message, response.Object, innerException: null);
    }

    private sealed class RecordingAgentAdminOperations(
        Func<int, Exception?>? getNamesFailure = null,
        Func<string, string, int, Exception?>? createFailure = null) : IAgentAdminOperations
    {
        private int _createCalls;

        public int GetAgentNamesCalls { get; private set; }
        public int VectorSearchCreateCalls { get; private set; }

        public Task<IReadOnlyList<string>> GetAgentNamesAsync(CancellationToken ct = default)
        {
            GetAgentNamesCalls++;
            var failure = getNamesFailure?.Invoke(GetAgentNamesCalls);
            return failure is null
                ? Task.FromResult<IReadOnlyList<string>>([])
                : Task.FromException<IReadOnlyList<string>>(failure);
        }

        public Task<ProvisionedAgentReference> CreateAgentVersionAsync(
            string name,
            string model,
            string instructions,
            ResponseTool[] tools,
            CancellationToken ct = default)
        {
            _createCalls++;
            if (name == AgentDefinitions.VectorSearchAgentName)
                VectorSearchCreateCalls++;

            var failure = createFailure?.Invoke(name, model, _createCalls);
            return failure is null
                ? Task.FromResult(new ProvisionedAgentReference(name, name switch
            {
                var value when value == AgentDefinitions.OrchestratorAgentName => "version-orchestrator",
                var value when value == AgentDefinitions.VectorSearchAgentName => "version-vector",
                var value when value == AgentDefinitions.WebSearchAgentName => "version-web",
                var value when value == AgentDefinitions.PDFSearchAgentName => "version-pdf",
                var value when value == AgentDefinitions.GraphQueryAgentName => "version-graph",
                _ => "version-unknown"
            }))
                : Task.FromException<ProvisionedAgentReference>(failure);
        }
    }
}
