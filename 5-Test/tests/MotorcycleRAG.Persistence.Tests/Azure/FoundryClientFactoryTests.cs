using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Core;
using MotorcycleRAG.Persistence.Azure;

namespace MotorcycleRAG.Persistence.Tests.Azure;

public class FoundryClientFactoryTests
{
    private static TokenCredential CreateMockCredential()
        => new Mock<TokenCredential>().Object;

    // ---- Constructor ----

    [Fact]
    public void Constructor_ShouldNotThrow()
    {
        var act = () => new FoundryClientFactory();
        act.Should().NotThrow();
    }

    // ---- CreateProjectClient ----

    [Fact]
    public void CreateProjectClient_WithNullEndpoint_ShouldThrowArgumentException()
    {
        var sut = new FoundryClientFactory();

        var act = () => sut.CreateProjectClient(null!, CreateMockCredential());

        act.Should().Throw<ArgumentException>()
            .WithParameterName("endpoint");
    }

    [Fact]
    public void CreateProjectClient_WithWhitespaceEndpoint_ShouldThrowArgumentException()
    {
        var sut = new FoundryClientFactory();

        var act = () => sut.CreateProjectClient("   ", CreateMockCredential());

        act.Should().Throw<ArgumentException>()
            .WithParameterName("endpoint");
    }

    [Fact]
    public void CreateProjectClient_WithEmptyEndpoint_ShouldThrowArgumentException()
    {
        var sut = new FoundryClientFactory();

        var act = () => sut.CreateProjectClient("", CreateMockCredential());

        act.Should().Throw<ArgumentException>()
            .WithParameterName("endpoint");
    }

    [Fact]
    public void CreateProjectClient_WithNullCredential_ShouldThrowArgumentNullException()
    {
        var sut = new FoundryClientFactory();

        var act = () => sut.CreateProjectClient("https://test.example.com/", null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("credential");
    }

    [Fact]
    public void CreateProjectClient_WithValidInputs_ShouldReturnNonNullClient()
    {
        var sut = new FoundryClientFactory();

        var result = sut.CreateProjectClient(
            "https://test-foundry.cognitiveservices.azure.com/",
            CreateMockCredential());

        result.Should().NotBeNull();
        result.Should().BeOfType<AIProjectClient>();
    }

    // ---- CreateOpenAIClient ----

    [Fact]
    public void CreateOpenAIClient_WithNullProjectClient_ShouldThrowArgumentNullException()
    {
        var sut = new FoundryClientFactory();

        var act = () => sut.CreateOpenAIClient(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("projectClient");
    }

    // ---- CreateConversationsClient ----

    [Fact]
    public void CreateConversationsClient_WithNullProjectClient_ShouldThrowArgumentNullException()
    {
        var sut = new FoundryClientFactory();

        var act = () => sut.CreateConversationsClient(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("projectClient");
    }
}

