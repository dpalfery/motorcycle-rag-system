using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using FluentAssertions;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Azure;
using Xunit;

#pragma warning disable S1244 // Floating point equality check is intentional for zero-vector detection
#pragma warning disable CA1861 // Constant array in test method is acceptable
#pragma warning disable S3881 // Dispose pattern in test class is intentionally simplified

namespace MotorcycleRAG.IntegrationTests.Persistence;

/// <summary>
/// Integration tests that verify the real AzureOpenAIClientWrapper → DeepInfra HTTP path
/// for embedding generation. Skipped by default; requires a real DEEPINFRA_API_KEY and network.
/// </summary>
[Trait("Category", "Integration")]
public class DeepInfraEmbeddingTests : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly NoOpDisposable _loggingScope = new();
    [Fact(Skip = "Integration test - requires real DeepInfra API key and network")]
    public async Task GetEmbeddingsAsync_WithRealDeepInfraApi_Returns3584DimVector()
    {
        // Arrange — read API key from environment (never hardcoded)
        var apiKey = Environment.GetEnvironmentVariable("DEEPINFRA_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // Defensive guard in case Skip is ever removed but key is missing
            Assert.Fail("DEEPINFRA_API_KEY environment variable is not set");
            return;
        }

        // Create real HttpClient via mock factory
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(_httpClient);

        // Resilience mock: pass through to the inner operation directly
        var resilienceMock = new Mock<IResilienceService>();
        resilienceMock
            .Setup(r => r.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<float[][]>>>(),
                It.IsAny<Func<Task<float[][]>>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<float[][]>>, Func<Task<float[][]>>?, string?, CancellationToken>(
                (_, operation, _, _, _) => operation());

        // Correlation service mock: return a dummy correlation ID and no-op logging scope
        var correlationMock = new Mock<ICorrelationService>();
        correlationMock
            .Setup(c => c.GetOrCreateCorrelationId())
            .Returns("test-correlation-id");
        correlationMock
            .Setup(c => c.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(_loggingScope);

        // Minimal valid AzureAIOptions
        var options = Options.Create(new AzureAIOptions
        {
            OpenAIEndpoint = "https://not-used-for-deepinfra.example.com/",
            SearchServiceEndpoint = "https://not-used.example.com/",
            DocumentIntelligenceEndpoint = "https://not-used.example.com/",
            Models = new ModelOptions(),
            Retry = new RetryOptions()
        });

        using var sut = new AzureOpenAIClientWrapper(
            options,
            NullLogger<AzureOpenAIClientWrapper>.Instance,
            resilienceMock.Object,
            correlationMock.Object,
            httpClientFactory.Object);

        // Act
        var result = await sut.GetEmbeddingsAsync(
            "Qwen/Qwen3-Embedding-4B",
            new[] { "Honda CBR 1000RR specifications" },
            CancellationToken.None);

        // Assert
        result.Should().HaveCount(1, "one embedding for one input text");
        result[0].Should().HaveCount(3584, "Qwen3-Embedding-4B produces 3584-dimensional vectors");
        result[0].Any(f => f != 0f).Should().BeTrue("real embeddings contain non-zero values");
    }

    /// <summary>
    /// No-op disposable for the correlation service logging scope mock.
    /// </summary>
    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _loggingScope.Dispose();
        GC.SuppressFinalize(this);
    }
}
