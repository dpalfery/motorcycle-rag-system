using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Application.Services.Web;

/// <summary>
/// Manages rate limiting for web search requests to prevent overwhelming sources
/// </summary>
public class WebSearchRateLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly ConcurrentDictionary<string, DateTime> _lastRequestTimes;
    private readonly TimeSpan _minRequestInterval;
    private readonly ILogger<WebSearchRateLimiter> _logger;
    private bool _disposed;

    public WebSearchRateLimiter(
        int maxConcurrentRequests,
        int minIntervalMs,
        ILogger<WebSearchRateLimiter> logger)
    {
        _semaphore = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
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
            await EnforceMinimumIntervalAsync();
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
        return new RateLimitToken(_semaphore);
    }

    private async Task EnforceMinimumIntervalAsync()
    {
        var now = DateTime.UtcNow;
        if (_lastRequestTimes.TryGetValue("global", out var lastRequest))
        {
            var timeSinceLastRequest = now - lastRequest;
            if (timeSinceLastRequest < _minRequestInterval)
            {
                var delay = _minRequestInterval - timeSinceLastRequest;
                _logger.LogDebug("Rate limiting: waiting {Delay}ms", delay.TotalMilliseconds);
                await Task.Delay(delay);
            }
        }
        _lastRequestTimes["global"] = DateTime.UtcNow;
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
            _semaphore?.Dispose();
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