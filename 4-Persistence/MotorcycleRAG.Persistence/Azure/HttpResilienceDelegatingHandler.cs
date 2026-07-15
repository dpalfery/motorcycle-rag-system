using Polly;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// DelegatingHandler that applies retry + circuit breaker resilience to outbound HTTP calls.
/// Retries on transient failures (5xx, 408, 429) with exponential backoff; opens the circuit
/// after 5 consecutive failures for 30 seconds.
/// </summary>
internal sealed class HttpResilienceDelegatingHandler : DelegatingHandler
{
    private readonly IAsyncPolicy<HttpResponseMessage> _combinedPolicy;

    /// <param name="retryDelay">
    /// Overrides the retry backoff delay computation. Defaults to the production exponential
    /// backoff (2^attempt seconds); tests inject a near-zero delay to avoid waiting out real
    /// multi-second backoffs while still exercising the real retry-count/circuit-breaker logic.
    /// </param>
    public HttpResilienceDelegatingHandler(Func<int, TimeSpan>? retryDelay = null)
    {
        _combinedPolicy = BuildPolicy(retryDelay ?? (attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => _combinedPolicy.ExecuteAsync(ct => base.SendAsync(request, ct), cancellationToken);

    private static IAsyncPolicy<HttpResponseMessage> BuildPolicy(Func<int, TimeSpan> retryDelay)
    {
        var retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .OrResult<HttpResponseMessage>(r =>
                (int)r.StatusCode >= 500
                || r.StatusCode == System.Net.HttpStatusCode.RequestTimeout
                || r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                3,
                retryDelay);

        var circuitBreakerPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .OrResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

        return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
    }
}
