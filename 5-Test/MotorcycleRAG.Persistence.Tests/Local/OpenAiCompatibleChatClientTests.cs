using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq.Contrib.HttpClient;
using Moq.Protected;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Local;

namespace MotorcycleRAG.Persistence.Tests.Local;

public class OpenAiCompatibleChatClientTests
{
    private static readonly ClassifierOptions ValidOptions = new()
    {
        Endpoint = "http://localhost:1234/v1",
        Model = "qwen3.5-9b",
        TimeoutSeconds = 30
    };

    private static readonly string ValidResponseJson = JsonSerializer.Serialize(new
    {
        choices = new[]
        {
            new { message = new { content = "Sport" } }
        }
    });

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenHttpClientFactoryIsNull()
    {
        // Act
        var act = () => new OpenAiCompatibleChatClient(
            null!,
            TestHelpers.OptionsFor(ValidOptions),
            Mock.Of<IConfiguration>(),
            TestHelpers.CreateNullLogger<OpenAiCompatibleChatClient>());

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsIsNull()
    {
        // Act
        var act = () => new OpenAiCompatibleChatClient(
            Mock.Of<IHttpClientFactory>(),
            null!,
            Mock.Of<IConfiguration>(),
            TestHelpers.CreateNullLogger<OpenAiCompatibleChatClient>());

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConfigurationIsNull()
    {
        // Act
        var act = () => new OpenAiCompatibleChatClient(
            Mock.Of<IHttpClientFactory>(),
            TestHelpers.OptionsFor(ValidOptions),
            null!,
            TestHelpers.CreateNullLogger<OpenAiCompatibleChatClient>());

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Act
        var act = () => new OpenAiCompatibleChatClient(
            Mock.Of<IHttpClientFactory>(),
            TestHelpers.OptionsFor(ValidOptions),
            Mock.Of<IConfiguration>(),
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task GetChatCompletionAsync_WhenSystemPromptIsNull_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = async () => await sut.GetChatCompletionAsync(null!, "user prompt");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("systemPrompt");
    }

    [Fact]
    public async Task GetChatCompletionAsync_WhenSystemPromptIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = async () => await sut.GetChatCompletionAsync("", "user prompt");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("systemPrompt");
    }

    [Fact]
    public async Task GetChatCompletionAsync_WhenUserPromptIsNull_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = async () => await sut.GetChatCompletionAsync("system prompt", null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("userPrompt");
    }

    [Fact]
    public async Task GetChatCompletionAsync_WhenUserPromptIsWhitespace_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = async () => await sut.GetChatCompletionAsync("system prompt", "   ");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("userPrompt");
    }

    [Fact]
    public async Task GetChatCompletionAsync_WhenEndpointNotConfigured_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ClassifierOptions { Endpoint = "", Model = "test-model" };
        var sut = CreateSut(options);

        // Act
        var act = async () => await sut.GetChatCompletionAsync("system", "user");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Endpoint*not configured*");
    }

    [Fact]
    public async Task GetChatCompletionAsync_WhenModelNotConfigured_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ClassifierOptions { Endpoint = "http://localhost:1234/v1", Model = "" };
        var sut = CreateSut(options);

        // Act
        var act = async () => await sut.GetChatCompletionAsync("system", "user");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Model*not configured*");
    }

    [Fact]
    public async Task GetChatCompletionAsync_WhenEndpointReturnsSuccess_ReturnsContent()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Post, "http://localhost:1234/v1/chat/completions")
            .ReturnsResponse(HttpStatusCode.OK, ValidResponseJson, "application/json");

        var sut = CreateSutWithHandler(handler);

        // Act
        var result = await sut.GetChatCompletionAsync("system prompt", "user prompt");

        // Assert
        result.Should().Be("Sport");
    }

    [Fact]
    public async Task GetChatCompletionAsync_WhenApiKeyIsConfigured_SendsAuthorizationHeader()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Post, "http://localhost:1234/v1/chat/completions")
            .ReturnsResponse(HttpStatusCode.OK, ValidResponseJson, "application/json");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Classifier:ApiKey", "test-api-key" }
            })
            .Build();

        var sut = CreateSutWithHandler(handler, config: config);

        // Act
        var result = await sut.GetChatCompletionAsync("system prompt", "user prompt");

        // Assert
        result.Should().Be("Sport");
        handler.VerifyRequest(HttpMethod.Post, "http://localhost:1234/v1/chat/completions", async request =>
        {
            var authHeader = request.Headers.Authorization;
            return authHeader != null
                   && authHeader.Scheme == "Bearer"
                   && authHeader.Parameter == "test-api-key";
        }, Times.Once());
    }

    [Fact]
    public async Task GetChatCompletionAsync_WhenNoApiKeyConfigured_DoesNotSendAuthorizationHeader()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Post, "http://localhost:1234/v1/chat/completions")
            .ReturnsResponse(HttpStatusCode.OK, ValidResponseJson, "application/json");

        var sut = CreateSutWithHandler(handler);

        // Act
        var result = await sut.GetChatCompletionAsync("system prompt", "user prompt");

        // Assert
        result.Should().Be("Sport");
        handler.VerifyRequest(HttpMethod.Post, "http://localhost:1234/v1/chat/completions", async request =>
        {
            return request.Headers.Authorization == null;
        }, Times.Once());
    }

    [Fact]
    public async Task GetChatCompletionAsync_WhenEndpointReturnsNonSuccess_ThrowsHttpRequestException()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Post, "http://localhost:1234/v1/chat/completions")
            .ReturnsResponse(HttpStatusCode.InternalServerError, "{\"error\":\"server error\"}", "application/json");

        var sut = CreateSutWithHandler(handler);

        // Act
        var act = async () => await sut.GetChatCompletionAsync("system", "user");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*500*");
    }

    [Fact]
    public async Task GetChatCompletionAsync_WhenResponseHasNoChoices_ReturnsEmptyString()
    {
        // Arrange
        var emptyChoicesJson = JsonSerializer.Serialize(new { choices = Array.Empty<object>() });
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Post, "http://localhost:1234/v1/chat/completions")
            .ReturnsResponse(HttpStatusCode.OK, emptyChoicesJson, "application/json");

        var sut = CreateSutWithHandler(handler);

        // Act
        var result = await sut.GetChatCompletionAsync("system", "user");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetChatCompletionAsync_WhenResponseMissingChoicesField_ReturnsEmptyString()
    {
        // Arrange
        var noChoicesJson = """{"other": "data"}""";
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Post, "http://localhost:1234/v1/chat/completions")
            .ReturnsResponse(HttpStatusCode.OK, noChoicesJson, "application/json");

        var sut = CreateSutWithHandler(handler);

        // Act
        var result = await sut.GetChatCompletionAsync("system", "user");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetChatCompletionAsync_WhenResponseHasChoiceWithoutContent_ReturnsEmptyString()
    {
        // Arrange
        var noContentJson = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { role = "assistant" } }
            }
        });
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Post, "http://localhost:1234/v1/chat/completions")
            .ReturnsResponse(HttpStatusCode.OK, noContentJson, "application/json");

        var sut = CreateSutWithHandler(handler);

        // Act
        var result = await sut.GetChatCompletionAsync("system", "user");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetChatCompletionAsync_WhenEndpointIsUnreachable_ThrowsHttpRequestException()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Post, "http://localhost:1234/v1/chat/completions")
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var sut = CreateSutWithHandler(handler);

        // Act
        var act = async () => await sut.GetChatCompletionAsync("system", "user");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Connection refused*");
    }

    [Fact]
    public async Task GetChatCompletionAsync_ShouldSendCorrectJsonPayload()
    {
        // Arrange
        string? capturedBody = null;

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri != null &&
                    r.RequestUri.ToString() == "http://localhost:1234/v1/chat/completions"),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken _) =>
            {
                capturedBody = await request.Content!.ReadAsStringAsync();
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ValidResponseJson, Encoding.UTF8, "application/json")
                };
                return response;
            });

        var sut = CreateSutWithHandler(handler);

        // Act
        await sut.GetChatCompletionAsync("You are helpful", "What is a Sport bike?");

        // Assert
        capturedBody.Should().NotBeNull();
        var doc = JsonDocument.Parse(capturedBody!);

        doc.RootElement.GetProperty("model").GetString().Should().Be("qwen3.5-9b");
        doc.RootElement.GetProperty("temperature").GetDouble().Should().Be(0.0);
        doc.RootElement.GetProperty("maxTokens").GetInt32().Should().Be(16);

        var messages = doc.RootElement.GetProperty("messages");
        messages[0].GetProperty("role").GetString().Should().Be("system");
        messages[0].GetProperty("content").GetString().Should().Be("You are helpful");
        messages[1].GetProperty("role").GetString().Should().Be("user");
        messages[1].GetProperty("content").GetString().Should().Be("What is a Sport bike?");
    }

    private static OpenAiCompatibleChatClient CreateSut(
        ClassifierOptions? options = null,
        IConfiguration? config = null)
    {
        return new OpenAiCompatibleChatClient(
            Mock.Of<IHttpClientFactory>(),
            TestHelpers.OptionsFor(options ?? ValidOptions),
            config ?? Mock.Of<IConfiguration>(),
            TestHelpers.CreateNullLogger<OpenAiCompatibleChatClient>());
    }

    private static OpenAiCompatibleChatClient CreateSutWithHandler(
        Mock<HttpMessageHandler> handler,
        IConfiguration? config = null)
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(OpenAiCompatibleChatClient.HttpClientName))
            .Returns(handler.CreateClient());

        return new OpenAiCompatibleChatClient(
            mockFactory.Object,
            TestHelpers.OptionsFor(ValidOptions),
            config ?? Mock.Of<IConfiguration>(),
            TestHelpers.CreateNullLogger<OpenAiCompatibleChatClient>());
    }
}
