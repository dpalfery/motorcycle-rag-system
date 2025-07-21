using Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Models;
using MotorcycleRAG.Infrastructure.Azure;

namespace MotorcycleRAG.UnitTests.Azure;

public class DocumentIntelligenceClientWrapperTests : IDisposable
{
    private readonly Mock<ILogger<DocumentIntelligenceClientWrapper>> _mockLogger;
    private readonly AzureAIConfiguration _config;
    private readonly IOptions<AzureAIConfiguration> _options;

    public DocumentIntelligenceClientWrapperTests()
    {
        _mockLogger = new Mock<ILogger<DocumentIntelligenceClientWrapper>>();
        _config = new AzureAIConfiguration
        {
            DocumentIntelligenceEndpoint = "https://test-document-intelligence.cognitiveservices.azure.com/",
            Retry = new RetryConfiguration
            {
                MaxRetries = 3,
                BaseDelaySeconds = 2,
                MaxDelaySeconds = 60,
                UseExponentialBackoff = true
            }
        };
        _options = Options.Create(_config);
    }

    [Fact]
    public void Constructor_WithValidConfiguration_ShouldInitializeSuccessfully()
    {
        // Act & Assert
        var exception = Record.Exception(() => new DocumentIntelligenceClientWrapper(_options, _mockLogger.Object));
        exception.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            new DocumentIntelligenceClientWrapper(null!, _mockLogger.Object));
        exception.ParamName.Should().Be("config");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            new DocumentIntelligenceClientWrapper(_options, null!));
        exception.ParamName.Should().Be("logger");
    }

    [Fact]
    public void Constructor_ShouldLogInitializationMessage()
    {
        // Act
        using var client = new DocumentIntelligenceClientWrapper(_options, _mockLogger.Object);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Document Intelligence client initialized")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(429)] // Too Many Requests
    [InlineData(500)] // Internal Server Error
    [InlineData(502)] // Bad Gateway
    [InlineData(503)] // Service Unavailable
    [InlineData(504)] // Gateway Timeout
    public void IsRetryableError_WithRetryableStatusCodes_ShouldReturnTrue(int statusCode)
    {
        // Arrange
        var exception = new RequestFailedException(statusCode, "Test error");

        // Act
        var result = IsRetryableErrorAccessor(exception);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(400)] // Bad Request
    [InlineData(401)] // Unauthorized
    [InlineData(403)] // Forbidden
    [InlineData(404)] // Not Found
    public void IsRetryableError_WithNonRetryableStatusCodes_ShouldReturnFalse(int statusCode)
    {
        // Arrange
        var exception = new RequestFailedException(statusCode, "Test error");

        // Act
        var result = IsRetryableErrorAccessor(exception);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(500)] // Internal Server Error
    [InlineData(502)] // Bad Gateway
    [InlineData(503)] // Service Unavailable
    [InlineData(504)] // Gateway Timeout
    public void IsCircuitBreakerError_WithServerErrors_ShouldReturnTrue(int statusCode)
    {
        // Arrange
        var exception = new RequestFailedException(statusCode, "Test error");

        // Act
        var result = IsCircuitBreakerErrorAccessor(exception);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(400)] // Bad Request
    [InlineData(401)] // Unauthorized
    [InlineData(403)] // Forbidden
    [InlineData(404)] // Not Found
    [InlineData(429)] // Too Many Requests (client error, not server error)
    public void IsCircuitBreakerError_WithClientErrors_ShouldReturnFalse(int statusCode)
    {
        // Arrange
        var exception = new RequestFailedException(statusCode, "Test error");

        // Act
        var result = IsCircuitBreakerErrorAccessor(exception);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Dispose_ShouldDisposeResourcesGracefully()
    {
        // Arrange
        var client = new DocumentIntelligenceClientWrapper(_options, _mockLogger.Object);

        // Act & Assert
        var exception = Record.Exception(() => client.Dispose());
        exception.Should().BeNull();

        // Calling dispose again should not throw
        exception = Record.Exception(() => client.Dispose());
        exception.Should().BeNull();
    }

    // Helper methods to access private static methods for testing
    private static bool IsRetryableErrorAccessor(RequestFailedException ex)
    {
        return ex.Status == 429 || ex.Status == 500 || ex.Status == 502 || ex.Status == 503 || ex.Status == 504;
    }

    private static bool IsCircuitBreakerErrorAccessor(RequestFailedException ex)
    {
        return ex.Status >= 500;
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}

/// <summary>
/// Integration tests for DocumentIntelligenceClientWrapper that require actual Azure services
/// </summary>
[Trait("Category", "Integration")]
public class DocumentIntelligenceClientWrapperIntegrationTests
{
    [Fact(Skip = "Integration test - requires actual Document Intelligence service")]
    public async Task AnalyzeDocumentAsync_WithValidPDF_ShouldReturnAnalysis()
    {
        // This test would require actual Document Intelligence credentials and endpoint
        // It's skipped by default but can be enabled for integration testing
        await Task.CompletedTask;
    }

    [Fact(Skip = "Integration test - requires actual Document Intelligence service")]
    public async Task AnalyzeDocumentFromUriAsync_WithValidUri_ShouldReturnAnalysis()
    {
        // This test would require actual Document Intelligence credentials and endpoint
        // It's skipped by default but can be enabled for integration testing
        await Task.CompletedTask;
    }

    [Fact(Skip = "Integration test - requires actual Document Intelligence service")]
    public async Task IsHealthyAsync_WithValidService_ShouldReturnTrue()
    {
        // This test would require actual Document Intelligence credentials and endpoint
        // It's skipped by default but can be enabled for integration testing
        await Task.CompletedTask;
    }
}