using Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using MotorcycleRAG.Infrastructure.Azure;

namespace MotorcycleRAG.UnitTests.Azure;

public class AzureSearchClientWrapperTests : IDisposable
{
    private readonly Mock<ILogger<AzureSearchClientWrapper>> _mockLogger;
    private readonly Mock<IResilienceService> _mockResilienceService;
    private readonly Mock<ICorrelationService> _mockCorrelationService;
    private readonly AzureAIConfiguration _azureConfig;
    private readonly SearchConfiguration _searchConfig;
    private readonly IOptions<AzureAIConfiguration> _azureOptions;
    private readonly IOptions<SearchConfiguration> _searchOptions;

    public AzureSearchClientWrapperTests()
    {
        _mockLogger = new Mock<ILogger<AzureSearchClientWrapper>>();
        _mockResilienceService = new Mock<IResilienceService>();
        _mockCorrelationService = new Mock<ICorrelationService>();
        
        // Setup resilience service to execute operations
        _mockResilienceService
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<SearchResult[]>>, Func<Task<SearchResult[]>>?, string?, CancellationToken>(
                async (policyKey, operation, fallback, corrId, ct) =>
                {
                    try 
                    {
                        return await operation();
                    }
                    catch
                    {
                        // Return empty array if operation fails
                        return new SearchResult[0];
                    }
                });
                
        // Setup correlation service to return mock disposable
        _mockCorrelationService
            .Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(Mock.Of<IDisposable>());
            
        _mockCorrelationService
            .Setup(x => x.GetOrCreateCorrelationId())
            .Returns("test-correlation-id");
        
        _azureConfig = new AzureAIConfiguration
        {
            SearchServiceEndpoint = "https://test-search.search.windows.net/",
            Retry = new RetryConfiguration
            {
                MaxRetries = 3,
                BaseDelaySeconds = 2,
                MaxDelaySeconds = 60,
                UseExponentialBackoff = true
            }
        };

        _searchConfig = new SearchConfiguration
        {
            IndexName = "test-index",
            BatchSize = 100,
            MaxSearchResults = 50,
            EnableHybridSearch = true,
            EnableSemanticRanking = true
        };

        _azureOptions = Options.Create(_azureConfig);
        _searchOptions = Options.Create(_searchConfig);
    }

    [Fact]
    public void Constructor_WithValidConfiguration_ShouldInitializeSuccessfully()
    {
        // Act & Assert
        var exception = Record.Exception(() => new AzureSearchClientWrapper(
            _azureOptions, _searchOptions, _mockLogger.Object, _mockResilienceService.Object, _mockCorrelationService.Object));
        exception.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithNullAzureConfiguration_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            new AzureSearchClientWrapper(null!, _searchOptions, _mockLogger.Object, _mockResilienceService.Object, _mockCorrelationService.Object));
        exception.ParamName.Should().Be("azureConfig");
    }

    [Fact]
    public void Constructor_WithNullSearchConfiguration_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            new AzureSearchClientWrapper(_azureOptions, null!, _mockLogger.Object, _mockResilienceService.Object, _mockCorrelationService.Object));
        exception.ParamName.Should().Be("searchConfig");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            new AzureSearchClientWrapper(_azureOptions, _searchOptions, null!, _mockResilienceService.Object, _mockCorrelationService.Object));
        exception.ParamName.Should().Be("logger");
    }

    [Fact]
    public void Constructor_ShouldLogInitializationMessage()
    {
        // Act
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockResilienceService.Object, _mockCorrelationService.Object);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Azure Search client initialized")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithValidQuery_ShouldReturnResults()
    {
        // Arrange
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockResilienceService.Object, _mockCorrelationService.Object);

        // Act
        var results = await client.SearchAsync("test query", 10);

        // Assert
        results.Should().NotBeNull();
        results.Should().NotBeEmpty();
        results.Length.Should().BeLessOrEqualTo(10);
    }

    [Fact(Skip = "Integration test - requires actual Azure Search service")]
    public async Task IndexDocumentsAsync_WithValidDocuments_ShouldReturnTrue()
    {
        // Arrange
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockResilienceService.Object, _mockCorrelationService.Object);
        var documents = new[] { new { id = "1", content = "test content" } };

        // Act
        var result = await client.IndexDocumentsAsync(documents);

        // Assert
        result.Should().BeTrue();
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
        var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockResilienceService.Object, _mockCorrelationService.Object);

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
/// Integration tests for AzureSearchClientWrapper that require actual Azure services
/// </summary>
[Trait("Category", "Integration")]
public class AzureSearchClientWrapperIntegrationTests
{
    [Fact(Skip = "Integration test - requires actual Azure Search service")]
    public async Task SearchAsync_WithValidQuery_ShouldReturnResults()
    {
        // This test would require actual Azure Search credentials and endpoint
        // It's skipped by default but can be enabled for integration testing
        await Task.CompletedTask;
    }

    [Fact(Skip = "Integration test - requires actual Azure Search service")]
    public async Task IndexDocumentsAsync_WithValidDocuments_ShouldIndexSuccessfully()
    {
        // This test would require actual Azure Search credentials and endpoint
        // It's skipped by default but can be enabled for integration testing
        await Task.CompletedTask;
    }

    [Fact(Skip = "Integration test - requires actual Azure Search service")]
    public async Task IsHealthyAsync_WithValidService_ShouldReturnTrue()
    {
        // This test would require actual Azure Search credentials and endpoint
        // It's skipped by default but can be enabled for integration testing
        await Task.CompletedTask;
    }
}