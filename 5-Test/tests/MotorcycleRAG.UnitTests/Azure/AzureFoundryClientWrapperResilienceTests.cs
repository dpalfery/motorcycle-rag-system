using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Azure;
using MotorcycleRAG.Persistence.Resilience;
using Polly.CircuitBreaker;
using Xunit;

using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Azure;

public class AzureFoundryClientWrapperResilienceTests : IDisposable {
    private readonly Mock<ILogger<AzureFoundryClientWrapper>> _mockLogger;
    private readonly Mock<IResilienceService> _mockResilienceService;
    private readonly Mock<ICorrelationService> _mockCorrelationService;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly AzureFoundryClientWrapper _client;

    public AzureFoundryClientWrapperResilienceTests() {
        _mockLogger = new Mock<ILogger<AzureFoundryClientWrapper>>();
        _mockResilienceService = new Mock<IResilienceService>();
        _mockCorrelationService = new Mock<ICorrelationService>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockConfiguration = new Mock<IConfiguration>();

        var config = new AzureFoundryOptions {
                        FoundryEndpoint = "https://test-foundry.cognitiveservices.azure.com/",
            SearchServiceEndpoint = "https://test-search.search.windows.net/",
            DocumentIntelligenceEndpoint = "https://test-docint.cognitiveservices.azure.com/",
            Models = new ModelOptions(),
            Retry = new RetryOptions()
        };

        var mockOptions = new Mock<IOptions<AzureFoundryOptions>>();
        mockOptions.Setup(x => x.Value).Returns(config);

        _client = new AzureFoundryClientWrapper(
            mockOptions.Object,
            _mockLogger.Object,
            _mockResilienceService.Object,
            _mockCorrelationService.Object,
            _mockHttpClientFactory.Object,
            _mockConfiguration.Object);
    }

    [Fact]
    public async Task GetEmbeddingsAsync_Success_ReturnsEmbeddings() {
        // Arrange
        var expectedEmbeddings = new[]
        {
            new[] { 0.1f, 0.2f, 0.3f },
            new[] { 0.4f, 0.5f, 0.6f }
        };
        const string correlationId = "test-correlation-789";
        var texts = new[] { "text1", "text2" };

        _mockCorrelationService
            .Setup(x => x.GetOrCreateCorrelationId())
            .Returns(correlationId);

        _mockResilienceService
            .Setup(x => x.ExecuteAsync(
                "AzureFoundry",
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
    public async Task GetEmbeddingsAsync_WithFallback_ReturnsZeroEmbeddings() {
        // Arrange
        const string correlationId = "test-correlation-fallback";
        var texts = new[] { "text1", "text2" };

        _mockCorrelationService
            .Setup(x => x.GetOrCreateCorrelationId())
            .Returns(correlationId);

        _mockResilienceService
            .Setup(x => x.ExecuteAsync(
                "AzureFoundry",
                It.IsAny<Func<Task<float[][]>>>(),
                It.IsAny<Func<Task<float[][]>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<float[][]>>, Func<Task<float[][]>>, string, CancellationToken>(
                async (_, _, fallback, _, _) => {
                    // Simulate circuit breaker triggering fallback
                    return await fallback();
                });

        // Act
        var result = await _client.GetEmbeddingsAsync("text-embedding-3-large", texts);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.All(result, embedding => {
            Assert.Equal(1536, embedding.Length);
            Assert.All(embedding, value => Assert.Equal(0f, value));
        });
    }

    [Fact]
    public async Task GetEmbeddingAsync_SingleText_ReturnsFirstEmbedding() {
        // Arrange
        var expectedEmbedding = new[] { 0.1f, 0.2f, 0.3f };
        const string correlationId = "test-correlation-single";
        const string text = "single text";

        _mockCorrelationService
            .Setup(x => x.GetOrCreateCorrelationId())
            .Returns(correlationId);

        _mockResilienceService
            .Setup(x => x.ExecuteAsync(
                "AzureFoundry",
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
    public async Task GetEmbeddingsAsync_CreatesLoggingScopeWithTextCount() {
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
                "AzureFoundry",
                It.IsAny<Func<Task<float[][]>>>(),
                It.IsAny<Func<Task<float[][]>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<float[][]>>, Func<Task<float[][]>>?, string?, CancellationToken>(
                async (_, operation, _, _, _) => {
                    // Execute the operation to trigger the CreateLoggingScope call
                    try {
                        return await operation();
                    }
                    catch {
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

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (disposing) {
            _client?.Dispose();
        }
    }
}
