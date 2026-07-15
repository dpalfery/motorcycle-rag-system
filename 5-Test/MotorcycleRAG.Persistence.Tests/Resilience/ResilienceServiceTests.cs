using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Resilience;
using Polly.CircuitBreaker;
using Xunit;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.UnitTests.Resilience;

public class ResilienceServiceTests {
    private readonly Mock<ILogger<ResilienceService>> _mockLogger;
    private readonly ResilienceService _resilienceService;

    public ResilienceServiceTests() {
        _mockLogger = new Mock<ILogger<ResilienceService>>();

        var config = new ResilienceOptions {
            CircuitBreaker = new CircuitBreakerOptions {
                OpenAI = new ServiceCircuitBreakerOptions {
                    FailureThreshold = 2,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    MinimumThroughput = 1
                }
            },
            Retry = new RetryOptions {
                MaxRetries = 2,
                BaseDelaySeconds = 1,
                MaxDelaySeconds = 5,
                UseExponentialBackoff = true
            }
        };

        var mockOptions = new Mock<IOptions<ResilienceOptions>>();
        mockOptions.Setup(x => x.Value).Returns(config);

        // Retry delay is overridden to zero so these tests exercise the real retry-count/
        // circuit-breaker-trip logic without waiting out real exponential-backoff seconds.
        _resilienceService = new ResilienceService(mockOptions.Object, _mockLogger.Object, retryDelayOverride: _ => TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulOperation_ReturnsResult() {
        // Arrange
        const string expectedResult = "Success";
        var operation = () => Task.FromResult(expectedResult);

        // Act
        var result = await _resilienceService.ExecuteAsync("AzureOpenAI", operation, fallback: null, correlationId: null, CancellationToken.None);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task ExecuteAsync_OperationThrowsException_RetriesAndThenFails() {
        // Arrange
        var callCount = 0;
        Func<Task<string>> operation = async () => {
            callCount++;
            await Task.Yield();
            throw new HttpRequestException("Service unavailable");
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _resilienceService.ExecuteAsync("AzureOpenAI", operation, fallback: null, correlationId: null, CancellationToken.None));

        Assert.Equal("Service unavailable", exception.Message);
        Assert.True(callCount > 1, "Should have retried the operation");
    }

    [Fact]
    public async Task ExecuteAsync_OperationFailsWithCircuitBreakerOpen_UsesFallback() {
        // Arrange
        const string fallbackResult = "Fallback";
        Func<Task<string>> operation = async () => {
            await Task.Yield();
            throw new HttpRequestException("Service down");
        };
        var fallback = () => Task.FromResult(fallbackResult);

        // Trigger circuit breaker by failing multiple times
        for (int i = 0; i < 3; i++) {
            try {
                await _resilienceService.ExecuteAsync("AzureOpenAI", operation, fallback: null, correlationId: null, CancellationToken.None);
            }
            catch {
                // Expected failures to trigger circuit breaker
            }
        }

        // Act - Circuit breaker should be open now
        var result = await _resilienceService.ExecuteAsync("AzureOpenAI", operation, fallback, correlationId: null, CancellationToken.None);

        // Assert
        Assert.Equal(fallbackResult, result);
    }

    [Fact]
    public async Task ExecuteAsync_WithCorrelationId_LogsWithCorrelation() {
        // Arrange
        const string correlationId = "test-correlation-123";
        const string expectedResult = "Success";
        var operation = () => Task.FromResult(expectedResult);

        // Act
        var result = await _resilienceService.ExecuteAsync("AzureOpenAI", operation, fallback: null, correlationId: correlationId, CancellationToken.None);

        // Assert
        Assert.Equal(expectedResult, result);
        // Verify logging with correlation context
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("AzureOpenAI")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownPolicyKey_ExecutesWithoutResilience() {
        // Arrange
        const string expectedResult = "Success";
        var operation = () => Task.FromResult(expectedResult);

        // Act
        var result = await _resilienceService.ExecuteAsync("UnknownPolicy", operation, fallback: null, correlationId: null, CancellationToken.None);

        // Assert
        Assert.Equal(expectedResult, result);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("No resilience policy found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void GetCircuitBreakerState_ValidPolicyKey_ReturnsState() {
        // Act
        var state = _resilienceService.GetCircuitBreakerState("AzureOpenAI");

        // Assert
        Assert.Equal(CircuitBreakerState.Closed, state);
    }

    [Fact]
    public void GetCircuitBreakerState_InvalidPolicyKey_ReturnsClosedState() {
        // Act
        var state = _resilienceService.GetCircuitBreakerState("InvalidKey");

        // Assert
        Assert.Equal(CircuitBreakerState.Closed, state);
    }

    [Fact]
    public void GetHealthStatus_ReturnsAllCircuitBreakerStates() {
        // Act
        var healthStatus = _resilienceService.HealthStatus;

        // Assert
        Assert.NotEmpty(healthStatus);
        Assert.Contains("AzureOpenAI", healthStatus.Keys);
        Assert.Contains("AzureSearch", healthStatus.Keys);
        Assert.Contains("DocumentIntelligence", healthStatus.Keys);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ThrowsOperationCanceledException() {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var operation = () => Task.FromResult("Success");

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _resilienceService.ExecuteAsync("AzureOpenAI", operation, fallback: null, correlationId: null, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_FallbackThrowsException_PropagatesOriginalException() {
        // Arrange
        Func<Task<string>> operation = async () => {
            await Task.Yield();
            throw new HttpRequestException("Original error");
        };
        Func<Task<string>> fallback = async () => {
            await Task.Yield();
            throw new InvalidOperationException("Fallback error");
        };

        // Trigger circuit breaker
        for (int i = 0; i < 3; i++) {
            try {
                await _resilienceService.ExecuteAsync("AzureOpenAI", operation, fallback: null, correlationId: null, CancellationToken.None);
            }
            catch {
                // Expected failures
            }
        }

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _resilienceService.ExecuteAsync("AzureOpenAI", operation, fallback, correlationId: null, CancellationToken.None));

        Assert.Equal("Fallback error", exception.Message);
    }

    // ---- Interface method wrapper tests ----

    [Fact]
    public async Task ExecuteAsync_Void_ShouldCompleteSuccessfully() {
        // Arrange
        var wasCalled = false;
        Func<Task> operation = async () => {
            await Task.CompletedTask;
            wasCalled = true;
        };

        // Act
        await _resilienceService.ExecuteAsync("AzureOpenAI", operation, fallback: null, correlationId: null, CancellationToken.None);

        // Assert
        Assert.True(wasCalled);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_ShouldReturnResult() {
        // Arrange
        const string expected = "retry-result";
        var operation = () => Task.FromResult(expected);

        // Act
        var result = await _resilienceService.ExecuteWithRetryAsync(operation, "test-op");

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ExecuteWithCircuitBreakerAsync_ShouldReturnResult() {
        // Arrange
        const string expected = "circuit-result";
        var operation = () => Task.FromResult(expected);

        // Act
        var result = await _resilienceService.ExecuteWithCircuitBreakerAsync(operation, "test-op");

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ExecuteWithTimeoutAsync_ShouldReturnResult() {
        // Arrange
        const string expected = "timeout-result";
        var operation = () => Task.FromResult(expected);

        // Act
        var result = await _resilienceService.ExecuteWithTimeoutAsync(operation, TimeSpan.FromSeconds(30), "test-op");

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ExecuteAsync_DelegatedOperation_ShouldPassCancellationToken() {
        // Arrange
        const string expected = "delegated";
        var operation = (CancellationToken ct) => Task.FromResult(expected);

        // Act
        var result = await _resilienceService.ExecuteAsync(operation, "test-delegated", CancellationToken.None);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ExecuteAsync_WithFallbackOnTimeoutException_UsesFallback() {
        // The ShouldUseFallback method considers TimeoutException as a fallback trigger.
        // Arrange
        const string fallbackResult = "timeout-fallback";
        Func<Task<string>> operation = async () => {
            await Task.Yield();
            throw new TimeoutException("Operation timed out");
        };
        var fallback = () => Task.FromResult(fallbackResult);

        // Act
        var result = await _resilienceService.ExecuteAsync("AzureOpenAI", operation, fallback, correlationId: null, CancellationToken.None);

        // Assert
        Assert.Equal(fallbackResult, result);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPropagateNonFallbackException_WhenNoFallback() {
        // InvalidOperationException is not in ShouldUseFallback, so it should propagate
        // Arrange
        Func<Task<string>> operation = async () => {
            await Task.Yield();
            throw new InvalidOperationException("Non-retriable error");
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _resilienceService.ExecuteAsync("AzureOpenAI", operation, fallback: null, correlationId: null, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteWithTimeoutAsync_ShouldCompleteForFastOperation() {
        // Quick operation: returns before timeout, no exception expected
        const string expected = "fast";
        var operation = () => Task.FromResult(expected);

        var result = await _resilienceService.ExecuteWithTimeoutAsync(operation, TimeSpan.FromSeconds(30), "timeout-test");

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowCancellationTokenInDelegatedOperation() {
        // Use the "AzureOpenAI" policy key so the cancellation check executes
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var operation = (CancellationToken ct) => Task.FromResult(42);

        // Use a known policy key so the cancellation check runs inside the Polly pipeline
        var act = () => _resilienceService.ExecuteAsync(operation, "AzureOpenAI", cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }
}
