using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using MotorcycleRAG.Infrastructure.Azure;
using MotorcycleRAG.Infrastructure.Resilience;
using Polly.CircuitBreaker;
using Xunit;

namespace MotorcycleRAG.UnitTests.Azure;

public class AzureOpenAIClientWrapperResilienceTests
{
    private readonly Mock<ILogger<AzureOpenAIClientWrapper>> _mockLogger;
    private readonly Mock<IResilienceService> _mockResilienceService;
    private readonly Mock<ICorrelationService> _mockCorrelationService;
    private readonly AzureOpenAIClientWrapper _client;

    public AzureOpenAIClientWrapperResilienceTests()
    {
        _mockLogger = new Mock<ILogger<AzureOpenAIClientWrapper>>();
        _mockResilienceService = new Mock<IResilienceService>();
        _mockCorrelationService = new Mock<ICorrelationService>();

        var config = new AzureAIConfiguration
        {
            OpenAIEndpoint = "https://test-openai.openai.azure.com/",
            FoundryEndpoint = "https://test-foundry.cognitiveservices.azure.com/",
            SearchServiceEndpoint = "https://test-search.search.windows.net/",
            DocumentIntelligenceEndpoint = "https://test-docint.cognitiveservices.azure.com/",
            Models = new ModelConfiguration(),
            Retry = new RetryConfiguration()
        };

        var mockOptions = new Mock<IOptions<AzureAIConfiguration>>();
        mockOptions.Setup(x => x.Value).Returns(config);

        _client = new AzureOpenAIClientWrapper(
            mockOptions.Object,
            _mockLogger.Object,
            _mockResilienceService.Object,
            _mockCorrelationService.Object);
    }

    [Fact]
    public async Task GetChatCompletionAsync_Success_ReturnsResult()
    {
        // Arrange
        const string expectedResponse = "Test response";
        const string correlationId = "test-correlation-123";
        
        _mockCorrelationService
            .Setup(x => x.GetOrCreateCorrelationId())
            .Returns(correlationId);

        _mockResilienceService
            .Setup(x => x.ExecuteAsync(
                "AzureOpenAI",
                It.IsAny<Func<Task<string>>>(),
                It.IsAny<Func<Task<string>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _client.GetChatCompletionAsync("gpt-4", "Test prompt");

        // Assert
        Assert.Equal(expectedResponse, result);
        _mockResilienceService.Verify(
            x => x.ExecuteAsync(
                "AzureOpenAI",
                It.IsAny<Func<Task<string>>>(),
                It.IsAny<Func<Task<string>>>(),
                correlationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetChatCompletionAsync_WithFallback_UsesFallbackOnFailure()
    {
        // Arrange
        const string fallbackResponse = "Fallback response: Unable to process request at this time. Please try again later.";
        const string correlationId = "test-correlation-456";
        
        _mockCorrelationService
            .Setup(x => x.GetOrCreateCorrelationId())
            .Returns(correlationId);

        _mockResilienceService
            .Setup(x => x.ExecuteAsync(
                "AzureOpenAI",
                It.IsAny<Func<Task<string>>>(),
                It.IsAny<Func<Task<string>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<string>>, Func<Task<string>>, string, CancellationToken>(
                async (policy, operation, fallback, corrId, token) =>
                {
                    // Simulate circuit breaker triggering fallback
                    return await fallback();
                });

        // Act
        var result = await _client.GetChatCompletionAsync("gpt-4", "Test prompt");

        // Assert
        Assert.Equal(fallbackResponse, result);
    }

    [Fact]
    public async Task GetEmbeddingsAsync_Success_ReturnsEmbeddings()
    {
        // Arrange
        var expectedEmbeddings = new[] 
        { 
            new float[] { 0.1f, 0.2f, 0.3f }, 
            new float[] { 0.4f, 0.5f, 0.6f } 
        };
        const string correlationId = "test-correlation-789";
        var texts = new[] { "text1", "text2" };
        
        _mockCorrelationService
            .Setup(x => x.GetOrCreateCorrelationId())
            .Returns(correlationId);

        _mockResilienceService
            .Setup(x => x.ExecuteAsync(
                "AzureOpenAI",
                It.IsAny<Func<Task<float[][]>>>(),
                It.IsAny<Func<Task<float[][]>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEmbeddings);

        // Act
        var result = await _client.GetEmbeddingsAsync("text-embedding-3-large", texts);

        // Assert
        Assert.Equal(expectedEmbeddings, result);
    }

    [Fact]
    public async Task GetEmbeddingsAsync_WithFallback_ReturnsZeroEmbeddings()
    {
        // Arrange
        const string correlationId = "test-correlation-fallback";
        var texts = new[] { "text1", "text2" };
        
        _mockCorrelationService
            .Setup(x => x.GetOrCreateCorrelationId())
            .Returns(correlationId);

        _mockResilienceService
            .Setup(x => x.ExecuteAsync(
                "AzureOpenAI",
                It.IsAny<Func<Task<float[][]>>>(),
                It.IsAny<Func<Task<float[][]>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<float[][]>>, Func<Task<float[][]>>, string, CancellationToken>(
                async (policy, operation, fallback, corrId, token) =>
                {
                    // Simulate circuit breaker triggering fallback
                    return await fallback();
                });

        // Act
        var result = await _client.GetEmbeddingsAsync("text-embedding-3-large", texts);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.All(result, embedding => 
        {
            Assert.Equal(1536, embedding.Length);
            Assert.All(embedding, value => Assert.Equal(0f, value));
        });
    }

    [Fact]
    public async Task GetEmbeddingAsync_SingleText_ReturnsFirstEmbedding()
    {
        // Arrange
        var expectedEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
        const string correlationId = "test-correlation-single";
        const string text = "single text";
        
        _mockCorrelationService
            .Setup(x => x.GetOrCreateCorrelationId())
            .Returns(correlationId);

        _mockResilienceService
            .Setup(x => x.ExecuteAsync(
                "AzureOpenAI",
                It.IsAny<Func<Task<float[][]>>>(),
                It.IsAny<Func<Task<float[][]>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { expectedEmbedding });

        // Act
        var result = await _client.GetEmbeddingAsync("text-embedding-3-large", text);

        // Assert
        Assert.Equal(expectedEmbedding, result);
    }

    [Fact]
    public async Task GetChatCompletionAsync_CreatesLoggingScope()
    {
        // Arrange
        const string correlationId = "test-correlation-scope";
        const string deploymentName = "gpt-4";
        const string prompt = "Test prompt";
        
        _mockCorrelationService
            .Setup(x => x.GetOrCreateCorrelationId())
            .Returns(correlationId);

        _mockCorrelationService
            .Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(Mock.Of<IDisposable>());

        _mockResilienceService
            .Setup(x => x.ExecuteAsync(
                "AzureOpenAI",
                It.IsAny<Func<Task<string>>>(),
                It.IsAny<Func<Task<string>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<string>>, Func<Task<string>>?, string?, CancellationToken>(
                async (policyKey, operation, fallback, corrId, ct) =>
                {
                    // Execute the operation to trigger the CreateLoggingScope call
                    try 
                    {
                        return await operation();
                    }
                    catch
                    {
                        // Return mock data if operation fails
                        return "Test response";
                    }
                });

        // Act
        await _client.GetChatCompletionAsync(deploymentName, prompt);

        // Assert
        _mockCorrelationService.Verify(
            x => x.CreateLoggingScope(It.Is<Dictionary<string, object>>(dict =>
                dict.ContainsKey("Operation") &&
                dict.ContainsKey("DeploymentName") &&
                dict["Operation"].ToString() == "GetChatCompletion" &&
                dict["DeploymentName"].ToString() == deploymentName)),
            Times.Once);
    }

    [Fact]
    public async Task GetEmbeddingsAsync_CreatesLoggingScopeWithTextCount()
    {
        // Arrange
        const string correlationId = "test-correlation-scope-embeddings";
        const string deploymentName = "text-embedding-3-large";
        var texts = new[] { "text1", "text2", "text3" };
        
        _mockCorrelationService
            .Setup(x => x.GetOrCreateCorrelationId())
            .Returns(correlationId);

        _mockCorrelationService
            .Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(Mock.Of<IDisposable>());

        _mockResilienceService
            .Setup(x => x.ExecuteAsync(
                "AzureOpenAI",
                It.IsAny<Func<Task<float[][]>>>(),
                It.IsAny<Func<Task<float[][]>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<float[][]>>, Func<Task<float[][]>>?, string?, CancellationToken>(
                async (policyKey, operation, fallback, corrId, ct) =>
                {
                    // Execute the operation to trigger the CreateLoggingScope call
                    try 
                    {
                        return await operation();
                    }
                    catch
                    {
                        // Return mock data if operation fails
                        return new[] { new float[1536], new float[1536], new float[1536] };
                    }
                });

        // Act
        await _client.GetEmbeddingsAsync(deploymentName, texts);

        // Assert
        _mockCorrelationService.Verify(
            x => x.CreateLoggingScope(It.Is<Dictionary<string, object>>(dict =>
                dict.ContainsKey("Operation") &&
                dict.ContainsKey("DeploymentName") &&
                dict.ContainsKey("TextCount") &&
                dict["Operation"].ToString() == "GetEmbeddings" &&
                dict["DeploymentName"].ToString() == deploymentName &&
                (int)dict["TextCount"] == 3)),
            Times.Once);
    }

    [Fact]
    public async Task GetChatCompletionAsync_WithCancellation_PassesCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        const string correlationId = "test-correlation-cancellation";
        
        _mockCorrelationService
            .Setup(x => x.GetOrCreateCorrelationId())
            .Returns(correlationId);

        _mockResilienceService
            .Setup(x => x.ExecuteAsync(
                "AzureOpenAI",
                It.IsAny<Func<Task<string>>>(),
                It.IsAny<Func<Task<string>>>(),
                correlationId,
                cts.Token))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _client.GetChatCompletionAsync("gpt-4", "Test prompt", cts.Token));
    }
}