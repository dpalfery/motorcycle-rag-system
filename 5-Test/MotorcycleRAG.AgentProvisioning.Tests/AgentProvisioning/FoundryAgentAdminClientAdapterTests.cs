using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.Projects.Agents;
using Moq;
using MotorcycleRAG.AgentProvisioning.Azure;
using Xunit;
using OpenAI.Responses;

namespace MotorcycleRAG.UnitTests.AgentProvisioning;

public class FoundryAgentAdminClientAdapterTests
{
    [Fact]
    public void Constructor_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FoundryAgentAdminClientAdapter(null!));
    }

    private static ClientResult<ProjectsAgentVersion> CreateAgentVersionResult(string name, string version)
    {
        var agentVersion = ProjectsAgentsModelFactory.ProjectsAgentVersion(
            metadata: null,
            id: Guid.NewGuid().ToString(),
            name: name,
            version: version,
            description: null,
            createdAt: DateTimeOffset.UtcNow,
            definition: null);

        return ClientResult.FromValue(agentVersion, new Mock<PipelineResponse>().Object);
    }

    [Fact]
    public async Task CreateAgentVersionAsync_WhenClientSucceeds_ReturnsProvisionedAgentReference()
    {
        // Arrange
        var mockClient = new Mock<AgentAdministrationClient>();
        mockClient
            .Setup(c => c.CreateAgentVersionAsync(
                It.IsAny<string>(),
                It.IsAny<ProjectsAgentVersionCreationOptions>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAgentVersionResult("OrchestratorAgent", "20260101120000"));

        var adapter = new FoundryAgentAdminClientAdapter(mockClient.Object);

        // Act
        var result = await adapter.CreateAgentVersionAsync(
            "OrchestratorAgent",
            "gpt-4o",
            "You are an orchestrator.",
            Array.Empty<ResponseTool>());

        // Assert
        Assert.Equal("OrchestratorAgent", result.Name);
        Assert.Equal("20260101120000", result.Version);
    }

    [Fact]
    public async Task CreateAgentVersionAsync_BuildsOptionsWithNameDescriptionAndMetadata()
    {
        // Arrange
        ProjectsAgentVersionCreationOptions? capturedOptions = null;
        string? capturedAgentName = null;

        var mockClient = new Mock<AgentAdministrationClient>();
        mockClient
            .Setup(c => c.CreateAgentVersionAsync(
                It.IsAny<string>(),
                It.IsAny<ProjectsAgentVersionCreationOptions>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ProjectsAgentVersionCreationOptions, string, CancellationToken>(
                (agentName, options, _, _) =>
                {
                    capturedAgentName = agentName;
                    capturedOptions = options;
                })
            .ReturnsAsync(CreateAgentVersionResult("VectorSearchAgent", "1"));

        var adapter = new FoundryAgentAdminClientAdapter(mockClient.Object);

        // Act
        await adapter.CreateAgentVersionAsync(
            "VectorSearchAgent",
            "gpt-4o-mini",
            "Search the vector index.",
            Array.Empty<ResponseTool>());

        // Assert
        Assert.Equal("VectorSearchAgent", capturedAgentName);
        Assert.NotNull(capturedOptions);
        Assert.Equal("Motorcycle RAG VectorSearchAgent", capturedOptions!.Description);
        Assert.Equal("motorcycle-rag", capturedOptions.Metadata["system"]);

        var definition = Assert.IsType<DeclarativeAgentDefinition>(capturedOptions.Definition);
        Assert.Equal("gpt-4o-mini", definition.Model);
        Assert.Equal("Search the vector index.", definition.Instructions);
    }

    [Fact]
    public async Task CreateAgentVersionAsync_WhenClientThrows_PropagatesException()
    {
        // Arrange
        var mockClient = new Mock<AgentAdministrationClient>();
        mockClient
            .Setup(c => c.CreateAgentVersionAsync(
                It.IsAny<string>(),
                It.IsAny<ProjectsAgentVersionCreationOptions>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Foundry service unavailable"));

        var adapter = new FoundryAgentAdminClientAdapter(mockClient.Object);

        // Act
        var act = () => adapter.CreateAgentVersionAsync(
            "WebSearchAgent",
            "gpt-4o",
            "Search the web.",
            Array.Empty<ResponseTool>());

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal("Foundry service unavailable", exception.Message);
    }

    [Fact]
    public async Task CreateAgentVersionAsync_WithTools_AddsAllToolsToDefinition()
    {
        // Arrange
        ProjectsAgentVersionCreationOptions? capturedOptions = null;

        var tools = new[]
        {
            ResponseTool.CreateFunctionTool(
                "search_documents",
                BinaryData.FromObjectAsJson(new { type = "object", properties = new { } }),
                false,
                "Searches documents."),
            ResponseTool.CreateFunctionTool(
                "search_graph",
                BinaryData.FromObjectAsJson(new { type = "object", properties = new { } }),
                false,
                "Searches the graph.")
        };

        var mockClient = new Mock<AgentAdministrationClient>();
        mockClient
            .Setup(c => c.CreateAgentVersionAsync(
                It.IsAny<string>(),
                It.IsAny<ProjectsAgentVersionCreationOptions>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ProjectsAgentVersionCreationOptions, string, CancellationToken>(
                (_, options, _, _) => capturedOptions = options)
            .ReturnsAsync(CreateAgentVersionResult("GraphQueryAgent", "1"));

        var adapter = new FoundryAgentAdminClientAdapter(mockClient.Object);

        // Act
        await adapter.CreateAgentVersionAsync(
            "GraphQueryAgent",
            "gpt-4o",
            "Query the graph.",
            tools);

        // Assert
        Assert.NotNull(capturedOptions);
        var definition = Assert.IsType<DeclarativeAgentDefinition>(capturedOptions!.Definition);
        Assert.Equal(2, definition.Tools.Count);
        Assert.Same(tools[0], definition.Tools[0]);
        Assert.Same(tools[1], definition.Tools[1]);
    }

    [Fact]
    public async Task CreateAgentVersionAsync_PassesCancellationTokenThrough()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;

        var mockClient = new Mock<AgentAdministrationClient>();
        mockClient
            .Setup(c => c.CreateAgentVersionAsync(
                It.IsAny<string>(),
                It.IsAny<ProjectsAgentVersionCreationOptions>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ProjectsAgentVersionCreationOptions, string, CancellationToken>(
                (_, _, _, ct) => capturedToken = ct)
            .ReturnsAsync(CreateAgentVersionResult("PDFSearchAgent", "1"));

        var adapter = new FoundryAgentAdminClientAdapter(mockClient.Object);

        // Act
        await adapter.CreateAgentVersionAsync(
            "PDFSearchAgent",
            "gpt-4o",
            "Search PDFs.",
            Array.Empty<ResponseTool>(),
            cts.Token);

        // Assert
        Assert.Equal(cts.Token, capturedToken);
    }

    [Fact]
    public async Task GetAgentNamesAsync_HappyPath_ReturnsAgentNames()
    {
        var agent1 = CreateAgentRecord("OrchestratorAgent");
        var agent2 = CreateAgentRecord("VectorSearchAgent");
        var pages = new List<IEnumerable<ProjectsAgentRecord>> { new List<ProjectsAgentRecord> { agent1, agent2 } };
        var asyncResult = AsyncCollectionResult<ProjectsAgentRecord>.FromPages(pages);

        var mockClient = new Mock<AgentAdministrationClient>();
        mockClient
            .Setup(c => c.GetAgentsAsync(
                null, null, null, null, null, It.IsAny<CancellationToken>()))
            .Returns(asyncResult);

        var adapter = new FoundryAgentAdminClientAdapter(mockClient.Object);

        var result = await adapter.GetAgentNamesAsync();

        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Should().Be("OrchestratorAgent");
        result[1].Should().Be("VectorSearchAgent");
    }

    [Fact]
    public async Task GetAgentNamesAsync_EmptyCollection_ReturnsEmptyList()
    {
        var pages = new List<IEnumerable<ProjectsAgentRecord>> { Array.Empty<ProjectsAgentRecord>() };
        var asyncResult = AsyncCollectionResult<ProjectsAgentRecord>.FromPages(pages);

        var mockClient = new Mock<AgentAdministrationClient>();
        mockClient
            .Setup(c => c.GetAgentsAsync(
                null, null, null, null, null, It.IsAny<CancellationToken>()))
            .Returns(asyncResult);

        var adapter = new FoundryAgentAdminClientAdapter(mockClient.Object);

        var result = await adapter.GetAgentNamesAsync();

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAgentNamesAsync_WhenClientThrows_PropagatesException()
    {
        var mockClient = new Mock<AgentAdministrationClient>();
        mockClient
            .Setup(c => c.GetAgentsAsync(
                null, null, null, null, null, It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Foundry service unavailable"));

        var adapter = new FoundryAgentAdminClientAdapter(mockClient.Object);

        var act = () => adapter.GetAgentNamesAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Foundry service unavailable");
    }

    [Fact]
    public async Task GetAgentNamesAsync_PassesCancellationTokenThrough()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;

        var mockClient = new Mock<AgentAdministrationClient>();
        mockClient
            .Setup(c => c.GetAgentsAsync(
                null, null, null, null, null, It.IsAny<CancellationToken>()))
            .Callback<ProjectsAgentKind?, int?, AgentListOrder?, string?, string?, CancellationToken>(
                (_, _, _, _, _, ct) => capturedToken = ct)
            .Returns(AsyncCollectionResult<ProjectsAgentRecord>.FromPages(
                new List<IEnumerable<ProjectsAgentRecord>> { Array.Empty<ProjectsAgentRecord>() }));

        var adapter = new FoundryAgentAdminClientAdapter(mockClient.Object);

        await adapter.GetAgentNamesAsync(cts.Token);

        capturedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task GetAgentNamesAsync_WithMultiplePages_CollectsAllNames()
    {
        var page1 = new List<ProjectsAgentRecord> { CreateAgentRecord("AgentA"), CreateAgentRecord("AgentB") };
        var page2 = new List<ProjectsAgentRecord> { CreateAgentRecord("AgentC") };
        var pages = new List<IEnumerable<ProjectsAgentRecord>> { page1, page2 };
        var asyncResult = AsyncCollectionResult<ProjectsAgentRecord>.FromPages(pages);

        var mockClient = new Mock<AgentAdministrationClient>();
        mockClient
            .Setup(c => c.GetAgentsAsync(
                null, null, null, null, null, It.IsAny<CancellationToken>()))
            .Returns(asyncResult);

        var adapter = new FoundryAgentAdminClientAdapter(mockClient.Object);

        var result = await adapter.GetAgentNamesAsync();

        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result[0].Should().Be("AgentA");
        result[1].Should().Be("AgentB");
        result[2].Should().Be("AgentC");
    }

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern ProjectsAgentRecord CreateProjectsAgentRecord();

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<Name>k__BackingField")]
    private static extern ref string GetNameField(ProjectsAgentRecord record);

    private static ProjectsAgentRecord CreateAgentRecord(string name)
    {
        var record = CreateProjectsAgentRecord();
        GetNameField(record) = name;
        return record;
    }
}
