using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Core;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Azure;

namespace MotorcycleRAG.Persistence.Tests.Azure;
public class FoundryAgentRunnerTests
{
    private static Mock<IFoundryClientFactory> CreateFactoryMock()
    {
        var factory = new Mock<IFoundryClientFactory>();
        // Return null for SDK types — the runner only stores them; construction-time
        // argument validation does not dereference the clients.
        factory.Setup(f => f.CreateProjectClient(It.IsAny<string>(), It.IsAny<TokenCredential>()))
            .Returns((Mock.Of<AIProjectClient>()));
        factory.Setup(f => f.CreateOpenAIClient(It.IsAny<AIProjectClient>()))
            .Returns(Mock.Of<ProjectOpenAIClient>());
        factory.Setup(f => f.CreateConversationsClient(It.IsAny<AIProjectClient>()))
            .Returns(Mock.Of<ProjectConversationsClient>());
        return factory;
    }

    private static IOptions<AzureFoundryOptions> CreateValidOptions(
        string endpoint = "https://foundry.example.com")
        => Options.Create(new AzureFoundryOptions { FoundryEndpoint = endpoint });

    private static IAzureCredentialProvider CreateCredentialProvider()
    {
        var provider = new Mock<IAzureCredentialProvider>();
        provider.Setup(x => x.GetDefaultCredential()).Returns(new global::Azure.Identity.DefaultAzureCredential());
        return provider.Object;
    }

    private static FoundryAgentRunner CreateRunner(
        IOptions<AzureFoundryOptions>? options = null,
        IFoundryClientFactory? factory = null)
    {
        return new FoundryAgentRunner(
            options ?? CreateValidOptions(),
            factory ?? CreateFactoryMock().Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<FoundryAgentRunner>());
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsIsNull()
    {
        var act = () => new FoundryAgentRunner(
            null!, CreateFactoryMock().Object, CreateCredentialProvider(), TestHelpers.CreateNullLogger<FoundryAgentRunner>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenFactoryIsNull()
    {
        var act = () => new FoundryAgentRunner(
            CreateValidOptions(), null!, CreateCredentialProvider(), TestHelpers.CreateNullLogger<FoundryAgentRunner>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("foundryClientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenCredentialProviderIsNull()
    {
        var act = () => new FoundryAgentRunner(
            CreateValidOptions(), CreateFactoryMock().Object, null!, TestHelpers.CreateNullLogger<FoundryAgentRunner>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("credentialProvider");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var act = () => new FoundryAgentRunner(
            CreateValidOptions(), CreateFactoryMock().Object, CreateCredentialProvider(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsValueIsNull()
    {
        var mock = new Mock<IOptions<AzureFoundryOptions>>();
        mock.SetupGet(o => o.Value).Returns((AzureFoundryOptions)null!);
        var act = () => new FoundryAgentRunner(
            mock.Object, CreateFactoryMock().Object, CreateCredentialProvider(), TestHelpers.CreateNullLogger<FoundryAgentRunner>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowInvalidOperationException_WhenFoundryEndpointIsEmpty()
    {
        var opts = Options.Create(new AzureFoundryOptions { FoundryEndpoint = "" });
        var act = () => new FoundryAgentRunner(
            opts, CreateFactoryMock().Object, CreateCredentialProvider(), TestHelpers.CreateNullLogger<FoundryAgentRunner>());
        act.Should().Throw<InvalidOperationException>().WithMessage("*FoundryEndpoint*required*");
    }

    [Fact]
    public void Constructor_ShouldInvokeFactory_WhenOptionsAreValid()
    {
        var factory = CreateFactoryMock();
        var credentialProvider = new Mock<IAzureCredentialProvider>();
        credentialProvider.Setup(x => x.GetDefaultCredential()).Returns(new global::Azure.Identity.DefaultAzureCredential());

        var runner = new FoundryAgentRunner(
            CreateValidOptions(), factory.Object, credentialProvider.Object, TestHelpers.CreateNullLogger<FoundryAgentRunner>());

        runner.Should().NotBeNull();
        factory.Verify(
            f => f.CreateProjectClient(
                "https://foundry.example.com",
                It.IsAny<TokenCredential>()),
            Times.Once);
        factory.Verify(f => f.CreateOpenAIClient(It.IsAny<AIProjectClient>()), Times.Once);
        factory.Verify(f => f.CreateConversationsClient(It.IsAny<AIProjectClient>()), Times.Once);
        credentialProvider.Verify(x => x.GetDefaultCredential(), Times.Once);
    }

    [Fact]
    public void Constructor_ShouldNotInvokeFactory_WhenEndpointIsMissing()
    {
        var factory = CreateFactoryMock();
        var opts = Options.Create(new AzureFoundryOptions { FoundryEndpoint = "" });

        var act = () => new FoundryAgentRunner(
            opts, factory.Object, CreateCredentialProvider(), TestHelpers.CreateNullLogger<FoundryAgentRunner>());

        act.Should().Throw<InvalidOperationException>();
        factory.Verify(
            f => f.CreateProjectClient(It.IsAny<string>(), It.IsAny<TokenCredential>()),
            Times.Never);
    }

    // ─── Factory error handling ───────────────────────────────────────

    [Fact]
    public void Constructor_ShouldPropagateException_WhenCreateProjectClientThrows()
    {
        var factory = new Mock<IFoundryClientFactory>();
        factory.Setup(f => f.CreateProjectClient(It.IsAny<string>(), It.IsAny<TokenCredential>()))
            .Throws(new InvalidOperationException("Project client failure"));

        var act = () => new FoundryAgentRunner(
            CreateValidOptions(), factory.Object, CreateCredentialProvider(), TestHelpers.CreateNullLogger<FoundryAgentRunner>());

        act.Should().Throw<InvalidOperationException>().WithMessage("Project client failure");
    }

    [Fact]
    public void Constructor_ShouldPropagateException_WhenCreateOpenAIClientThrows()
    {
        var factory = new Mock<IFoundryClientFactory>();
        var projectClient = Mock.Of<AIProjectClient>();
        factory.Setup(f => f.CreateProjectClient(It.IsAny<string>(), It.IsAny<TokenCredential>()))
            .Returns(projectClient);
        factory.Setup(f => f.CreateOpenAIClient(It.IsAny<AIProjectClient>()))
            .Throws(new InvalidOperationException("OpenAI client failure"));

        var act = () => new FoundryAgentRunner(
            CreateValidOptions(), factory.Object, CreateCredentialProvider(), TestHelpers.CreateNullLogger<FoundryAgentRunner>());

        act.Should().Throw<InvalidOperationException>().WithMessage("OpenAI client failure");
    }

    [Fact]
    public void Constructor_ShouldPropagateException_WhenCreateConversationsClientThrows()
    {
        var factory = new Mock<IFoundryClientFactory>();
        var projectClient = Mock.Of<AIProjectClient>();
        factory.Setup(f => f.CreateProjectClient(It.IsAny<string>(), It.IsAny<TokenCredential>()))
            .Returns(projectClient);
        factory.Setup(f => f.CreateConversationsClient(It.IsAny<AIProjectClient>()))
            .Throws(new InvalidOperationException("Conversations client failure"));

        var act = () => new FoundryAgentRunner(
            CreateValidOptions(), factory.Object, CreateCredentialProvider(), TestHelpers.CreateNullLogger<FoundryAgentRunner>());

        act.Should().Throw<InvalidOperationException>().WithMessage("Conversations client failure");
    }

    // ─── SendAgentMessageAsync argument validation ────────────────────

    [Fact]
    public async Task SendAgentMessageAsync_ShouldThrowArgumentException_WhenContentIsEmpty()
    {
        var sut = CreateRunner();

        var act = () => sut.SendAgentMessageAsync("conv", "agent", "");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("content");
    }

    [Fact]
    public async Task SendAgentMessageAsync_ShouldThrowArgumentException_WhenContentIsNull()
    {
        var sut = CreateRunner();

        var act = () => sut.SendAgentMessageAsync("conv", "agent", null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("content");
    }

    [Fact]
    public async Task SendAgentMessageAsync_ShouldThrowArgumentException_WhenConversationIdIsEmpty()
    {
        var sut = CreateRunner();

        var act = () => sut.SendAgentMessageAsync("", "agent", "valid content");

        // Empty conversationId is validated in the private CreateAgentResponseAsync,
        // but the exception type is the same — ArgumentException.
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendAgentMessageAsync_ShouldThrowArgumentException_WhenAgentNameIsEmpty()
    {
        var sut = CreateRunner();

        var act = () => sut.SendAgentMessageAsync("conv", "", "valid content");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── SubmitToolOutputsAsync argument validation ───────────────────

    [Fact]
    public async Task SubmitToolOutputsAsync_ShouldThrowArgumentNullException_WhenOutputsIsNull()
    {
        var sut = CreateRunner();

        var act = () => sut.SubmitToolOutputsAsync("conv", "agent", null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("outputs");
    }

    // ─── DeleteConversationAsync argument validation ──────────────────

    [Fact]
    public async Task DeleteConversationAsync_ShouldThrowArgumentException_WhenConversationIdIsEmpty()
    {
        var sut = CreateRunner();

        var act = () => sut.DeleteConversationAsync("");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("conversationId");
    }

    [Fact]
    public async Task DeleteConversationAsync_ShouldThrowArgumentException_WhenConversationIdIsNull()
    {
        var sut = CreateRunner();

        var act = () => sut.DeleteConversationAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("conversationId");
    }

    // ─── Constructor with agent names configured ──────────────────────

    [Fact]
    public void Constructor_ShouldSucceed_WithAgentNamesAndVersionsConfigured()
    {
        var opts = Options.Create(new AzureFoundryOptions
        {
            FoundryEndpoint = "https://foundry.example.com",
            OrchestratorAgentName = "orchestrator",
            OrchestratorAgentVersion = "v1",
            VectorSearchAgentName = "vector-search",
            VectorSearchAgentVersion = "v2"
        });

        var act = () => new FoundryAgentRunner(
            opts, CreateFactoryMock().Object, CreateCredentialProvider(), TestHelpers.CreateNullLogger<FoundryAgentRunner>());

        act.Should().NotThrow();
    }

    // ─── CreateConversationAsync ─────────────────────────────────────
    // BLOCKER: ProjectConversationsClient.CreateProjectConversationAsync is likely
    // non-virtual (Azure SDK sealed internals). Mock.Of<T>() creates a loose mock that
    // cannot intercept non-virtual instance methods. At runtime the call would fall
    // through to the real SDK implementation and fail without a live Azure endpoint.
    //
    // These tests validate the argument validation and constructor seams only.
    // Full CreateConversationAsync coverage requires either:
    //   (a) an integration test against a live Foundry endpoint, or
    //   (b) an abstraction (e.g. IProjectConversationsClient interface) wrapping
    //       the sealed SDK type so the test can inject a test double.

    [Fact]
    public async Task SendAgentMessageAsync_ShouldThrowArgumentException_WhenAgentNameIsNull()
    {
        var sut = CreateRunner();

        var act = () => sut.SendAgentMessageAsync("conv", null!, "content");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("agentName");
    }

    [Fact]
    public async Task SubmitToolOutputsAsync_ShouldThrowArgumentException_WhenConversationIdIsEmpty()
    {
        var sut = CreateRunner();

        var act = () => sut.SubmitToolOutputsAsync("", "agent",
            new[] { new AgentToolOutput("call1", "result") });

        // The empty check happens inside CreateAgentResponseAsync, surfaced as ArgumentException
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("conversationId");
    }

    // ─── DeleteConversationAsync: 404 exception swallowing ───────────
    // BLOCKER: ProjectConversationsClient.DeleteConversationAsync is likely non-virtual.
    // The try/catch for ClientResultException(404) branch cannot be reached through a Moq
    // mock because Moq cannot intercept non-virtual methods. This leaves the 404-swallow and
    // non-404-throw branches untestable without an integration test or a wrapper interface.

    [Fact]
    public async Task DeleteConversationAsync_ShouldThrowArgumentNullException_WhenConversationIdIsNull_Async()
    {
        var sut = CreateRunner();

        var act = () => sut.DeleteConversationAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("conversationId");
    }

    [Fact]
    public async Task DeleteConversationAsync_ShouldThrowArgumentException_WhenConversationIdIsWhitespace()
    {
        var sut = CreateRunner();

        var act = () => sut.DeleteConversationAsync("   ");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("conversationId");
    }

    // ─── SubmitToolOutputsAsync additional argument validation ─────────

    [Fact]
    public async Task SubmitToolOutputsAsync_ShouldThrowArgumentException_WhenAgentNameIsEmpty()
    {
        var sut = CreateRunner();

        var act = () => sut.SubmitToolOutputsAsync("conv", "",
            new[] { new AgentToolOutput("call1", "result") });

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("agentName");
    }

    [Fact]
    public async Task SubmitToolOutputsAsync_ShouldThrowArgumentException_WhenAgentNameIsNull()
    {
        var sut = CreateRunner();

        var act = () => sut.SubmitToolOutputsAsync("conv", null!,
            new[] { new AgentToolOutput("call1", "result") });

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("agentName");
    }

    [Fact]
    public async Task SubmitToolOutputsAsync_ShouldThrowArgumentException_WhenConversationIdIsNull()
    {
        var sut = CreateRunner();

        var act = () => sut.SubmitToolOutputsAsync(null!, "agent",
            new[] { new AgentToolOutput("call1", "result") });

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("conversationId");
    }

    [Fact]
    public async Task SubmitToolOutputsAsync_WithEmptyOutputs_ShouldThrowArgumentException_WhenConversationIdIsEmpty_AfterValidation()
    {
        var sut = CreateRunner();

        var act = () => sut.SubmitToolOutputsAsync("", "agent",
            Array.Empty<AgentToolOutput>());

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("conversationId");
    }
}
