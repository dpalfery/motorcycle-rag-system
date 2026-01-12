using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Application.Services.Web;

/// <summary>
/// Manages rate limiting for web search requests to prevent overwhelming sources
/// </summary>
public class WebSearchRateLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly SemaphoreSlim _intervalSemaphore;
    private readonly ConcurrentDictionary<string, DateTime> _lastRequestTimes;
    private readonly TimeSpan _minRequestInterval;
    private readonly ILogger<WebSearchRateLimiter> _logger;
    private bool _disposed;

    public WebSearchRateLimiter(
        IOptions<WebSearchOptions> options,
        ILogger<WebSearchRateLimiter> logger)
        : this(
            (options ?? throw new ArgumentNullException(nameof(options))).Value.MaxConcurrentRequests,
            options.Value.MinRequestIntervalMs,
            logger)
    {
    }

    public WebSearchRateLimiter(
        int maxConcurrentRequests,
        int minIntervalMs,
        ILogger<WebSearchRateLimiter> logger)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentRequests, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(minIntervalMs, 0);
        ArgumentNullException.ThrowIfNull(logger);

        _semaphore = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        _intervalSemaphore = new SemaphoreSlim(1, 1);
        _lastRequestTimes = new ConcurrentDictionary<string, DateTime>();
        _minRequestInterval = TimeSpan.FromMilliseconds(minIntervalMs);
        _logger = logger;
    }

    public Task<T> ExecuteWithRateLimitAsync<T>(Func<Task<T>> operation)
        => ExecuteWithRateLimitAsync(operation, CancellationToken.None);

    public async Task<T> ExecuteWithRateLimitAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnforceMinimumIntervalAsync(cancellationToken);
            return await operation();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IDisposable> AcquireAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            await EnforceMinimumIntervalAsync(CancellationToken.None);
            return new RateLimitToken(_semaphore);
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }

    private async Task EnforceMinimumIntervalAsync(CancellationToken cancellationToken)
    {
        await _intervalSemaphore.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            if (_lastRequestTimes.TryGetValue("global", out var lastRequest))
            {
                var timeSinceLastRequest = now - lastRequest;
                if (timeSinceLastRequest < _minRequestInterval)
                {
                    var delay = _minRequestInterval - timeSinceLastRequest;
                    _logger.LogDebug("Rate limiting: waiting {Delay}ms", delay.TotalMilliseconds);
                    await Task.Delay(delay, cancellationToken);
                    now = DateTime.UtcNow;
                }
            }

            _lastRequestTimes["global"] = now;
        }
        finally
        {
            _intervalSemaphore.Release();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _semaphore.Dispose();
            _intervalSemaphore.Dispose();
        }

        _disposed = true;
    }

    private sealed class RateLimitToken : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public RateLimitToken(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _semaphore.Release();
            }

            _disposed = true;
        }
    }
}
