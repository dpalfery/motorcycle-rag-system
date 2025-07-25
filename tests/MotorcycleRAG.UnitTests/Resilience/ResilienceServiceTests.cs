using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Core.Models;
using MotorcycleRAG.Infrastructure.Resilience;
using Polly.CircuitBreaker;
using Xunit;

namespace MotorcycleRAG.UnitTests.Resilience;

public class ResilienceServiceTests
{
    private readonly Mock<ILogger<ResilienceService>> _mockLogger;
    private readonly ResilienceService _resilienceService;

    public ResilienceServiceTests()
    {
        _mockLogger = new Mock<ILogger<ResilienceService>>();
        
        var config = new ResilienceConfiguration
        {
            CircuitBreaker = new CircuitBreakerConfiguration
            {
                OpenAI = new ServiceCircuitBreakerConfig
                {
                    FailureThreshold = 2,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    MinimumThroughput = 1
                }
            },
            Retry = new RetryConfiguration
            {
                MaxRetries = 2,
                BaseDelaySeconds = 1,
                MaxDelaySeconds = 5,
                UseExponentialBackoff = true
            }
        };

        var mockOptions = new Mock<IOptions<ResilienceConfiguration>>();
        mockOptions.Setup(x => x.Value).Returns(config);

        _resilienceService = new ResilienceService(mockOptions.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulOperation_ReturnsResult()
    {
        // Arrange
        const string expectedResult = "Success";
        var operation = () => Task.FromResult(expectedResult);

        // Act
        var result = await _resilienceService.ExecuteAsync("AzureOpenAI", operation);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task ExecuteAsync_OperationThrowsException_RetriesAndThenFails()
    {
        // Arrange
        var callCount = 0;
        var operation = () =>
        {
            callCount++;
            throw new HttpRequestException("Service unavailable");
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _resilienceService.ExecuteAsync("AzureOpenAI", operation));
        
        Assert.Equal("Service unavailable", exception.Message);
        Assert.True(callCount > 1, "Should have retried the operation");
    }

    [Fact]
    public async Task ExecuteAsync_OperationFailsWithCircuitBreakerOpen_UsesFallback()
    {
        // Arrange
        const string fallbackResult = "Fallback";
        var operation = () => throw new HttpRequestException("Service down");
        var fallback = () => Task.FromResult(fallbackResult);

        // Trigger circuit breaker by failing multiple times
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await _resilienceService.ExecuteAsync("AzureOpenAI", operation);
            }
            catch
            {
                // Expected failures to trigger circuit breaker
            }
        }

        // Act - Circuit breaker should be open now
        var result = await _resilienceService.ExecuteAsync("AzureOpenAI", operation, fallback);

        // Assert
        Assert.Equal(fallbackResult, result);
    }

    [Fact]
    public async Task ExecuteAsync_WithCorrelationId_LogsWithCorrelation()
    {
        // Arrange
        const string correlationId = "test-correlation-123";
        const string expectedResult = "Success";
        var operation = () => Task.FromResult(expectedResult);

        // Act
        var result = await _resilienceService.ExecuteAsync("AzureOpenAI", operation, null, correlationId);

        // Assert
        Assert.Equal(expectedResult, result);
        // Verify logging with correlation context
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("AzureOpenAI")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownPolicyKey_ExecutesWithoutResilience()
    {
        // Arrange
        const string expectedResult = "Success";
        var operation = () => Task.FromResult(expectedResult);

        // Act
        var result = await _resilienceService.ExecuteAsync("UnknownPolicy", operation);

        // Assert
        Assert.Equal(expectedResult, result);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("No resilience policy found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void GetCircuitBreakerState_ValidPolicyKey_ReturnsState()
    {
        // Act
        var state = _resilienceService.GetCircuitBreakerState("AzureOpenAI");

        // Assert
        Assert.Equal(CircuitBreakerState.Closed, state);
    }

    [Fact]
    public void GetCircuitBreakerState_InvalidPolicyKey_ReturnsClosedState()
    {
        // Act
        var state = _resilienceService.GetCircuitBreakerState("InvalidKey");

        // Assert
        Assert.Equal(CircuitBreakerState.Closed, state);
    }

    [Fact]
    public void GetHealthStatus_ReturnsAllCircuitBreakerStates()
    {
        // Act
        var healthStatus = _resilienceService.GetHealthStatus();

        // Assert
        Assert.NotEmpty(healthStatus);
        Assert.Contains("AzureOpenAI", healthStatus.Keys);
        Assert.Contains("AzureSearch", healthStatus.Keys);
        Assert.Contains("DocumentIntelligence", healthStatus.Keys);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        var operation = () => Task.FromResult("Success");

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _resilienceService.ExecuteAsync("AzureOpenAI", operation, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_FallbackThrowsException_PropagatesOriginalException()
    {
        // Arrange
        var operation = () => throw new HttpRequestException("Original error");
        var fallback = () => throw new InvalidOperationException("Fallback error");

        // Trigger circuit breaker
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await _resilienceService.ExecuteAsync("AzureOpenAI", operation);
            }
            catch
            {
                // Expected failures
            }
        }

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _resilienceService.ExecuteAsync("AzureOpenAI", operation, fallback));
        
        Assert.Equal("Fallback error", exception.Message);
    }
}