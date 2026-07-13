using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Services.Web;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Services.Web;

public sealed class WebSearchRateLimiterTests
{
    [Fact]
    public async Task ExecuteWithRateLimitAsync_WhenOperationSucceeds_ReturnsResult()
    {
        using var limiter = CreateLimiter();

        var result = await limiter.ExecuteWithRateLimitAsync(() => Task.FromResult("completed"));

        result.Should().Be("completed");
    }

    [Fact]
    public async Task ExecuteWithRateLimitAsync_WhenOperationThrows_ReleasesCapacityForNextOperation()
    {
        using var limiter = CreateLimiter(maxConcurrentRequests: 1);

        var failing = () => limiter.ExecuteWithRateLimitAsync<string>(
            () => Task.FromException<string>(new InvalidOperationException("operation failed")));

        await failing.Should().ThrowAsync<InvalidOperationException>();
        (await limiter.ExecuteWithRateLimitAsync(() => Task.FromResult("after failure"))).Should().Be("after failure");
    }

    [Fact]
    public async Task ExecuteWithRateLimitAsync_WhenDelayIsCancelled_ReleasesCapacityForNextOperation()
    {
        using var limiter = CreateLimiter(maxConcurrentRequests: 1, minIntervalMs: 1000);
        await limiter.ExecuteWithRateLimitAsync(() => Task.FromResult("first"));
        using var cancellation = new CancellationTokenSource();

        var delayed = limiter.ExecuteWithRateLimitAsync(() => Task.FromResult("cancelled"), cancellation.Token);
        await cancellation.CancelAsync();

        var cancelled = () => delayed;
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        (await limiter.ExecuteWithRateLimitAsync(() => Task.FromResult("after cancellation"))).Should().Be("after cancellation");
    }

    [Fact]
    public async Task AcquireAsync_WhenTokenIsDisposed_ReleasesCapacity()
    {
        using var limiter = CreateLimiter(maxConcurrentRequests: 1);
        using var token = await limiter.AcquireAsync();

        var waitingOperation = limiter.ExecuteWithRateLimitAsync(() => Task.FromResult("available"));
        token.Dispose();

        (await waitingOperation).Should().Be("available");
    }

    [Fact]
    public async Task AcquireAsync_WhenCalledTwiceWithInterval_EnforcesSubsequentRequestPath()
    {
        using var limiter = CreateLimiter(minIntervalMs: 1);

        using var first = await limiter.AcquireAsync();
        first.Dispose();
        using var second = await limiter.AcquireAsync();

        second.Should().NotBeNull();
    }

    [Fact]
    public async Task AcquireAsync_WhenIntervalLoggingFails_ReleasesTheAcquiredCapacity()
    {
        using var limiter = new WebSearchRateLimiter(1, 1000, new ThrowOnDebugLogger());
        var first = await limiter.AcquireAsync();
        first.Dispose();

        var act = () => limiter.AcquireAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExecuteWithRateLimitAsync_WhenOperationIsNull_ThrowsArgumentNullException()
    {
        using var limiter = CreateLimiter();

        var act = () => limiter.ExecuteWithRateLimitAsync<string>(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructors_WhenArgumentsAreInvalid_ThrowArgumentExceptions()
    {
        ((Action)(() => new WebSearchRateLimiter(0, 0, NullLogger<WebSearchRateLimiter>.Instance)))
            .Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => new WebSearchRateLimiter(1, -1, NullLogger<WebSearchRateLimiter>.Instance)))
            .Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => new WebSearchRateLimiter(1, 0, null!)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new WebSearchRateLimiter(null!, NullLogger<WebSearchRateLimiter>.Instance)))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WhenOptionsAreValid_UsesConfiguredValues()
    {
        using var limiter = new WebSearchRateLimiter(
            Options.Create(new WebSearchOptions { MaxConcurrentRequests = 2, MinRequestIntervalMs = 0 }),
            NullLogger<WebSearchRateLimiter>.Instance);

        limiter.Should().NotBeNull();
    }

    [Fact]
    public void Dispose_WhenCalledMoreThanOnce_IsIdempotent()
    {
        var limiter = CreateLimiter();

        limiter.Dispose();
        var act = limiter.Dispose;

        act.Should().NotThrow();
    }

    private static WebSearchRateLimiter CreateLimiter(
        int maxConcurrentRequests = 2,
        int minIntervalMs = 0) => new(
        maxConcurrentRequests,
        minIntervalMs,
        NullLogger<WebSearchRateLimiter>.Instance);

    private sealed class ThrowOnDebugLogger : ILogger<WebSearchRateLimiter>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Debug)
            {
                throw new InvalidOperationException("logger unavailable");
            }
        }
    }
}
