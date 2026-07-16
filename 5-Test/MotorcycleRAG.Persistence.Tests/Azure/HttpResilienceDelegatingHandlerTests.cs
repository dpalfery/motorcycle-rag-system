using System.Net;
using Moq.Contrib.HttpClient;
using MotorcycleRAG.Persistence.Azure;
using Polly.CircuitBreaker;

namespace MotorcycleRAG.Persistence.Tests.Azure;
public class HttpResilienceDelegatingHandlerTests
{
    private static HttpClient CreateClientWithHandler(Mock<HttpMessageHandler> handler)
    {
        // Zero retry delay: these tests assert on retry count/status-code behavior, not on
        // backoff timing, so there's no reason to wait out real exponential-backoff seconds.
        return new HttpClient(new HttpResilienceDelegatingHandler(retryDelay: _ => TimeSpan.Zero) { InnerHandler = handler.Object });
    }

    [Fact]
    public async Task SendAsync_ShouldReturnResponse_WhenSuccessful()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, "https://example.com/api/health")
            .ReturnsResponse(HttpStatusCode.OK);
        using var client = CreateClientWithHandler(handler);
        var response = await client.GetAsync(new Uri("https://example.com/api/health"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_ShouldRetryOnInternalServerError()
    {
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("success") });
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, "https://example.com/api/data")
            .Returns(() => Task.FromResult(responses.Dequeue()));
        using var client = CreateClientWithHandler(handler);
        var response = await client.GetAsync(new Uri("https://example.com/api/data"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("success");
        handler.VerifyRequest(HttpMethod.Get, "https://example.com/api/data", Times.Exactly(3));
    }

    [Fact]
    public async Task SendAsync_ShouldRetryOnServiceUnavailable_503()
    {
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("recovered") });
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, "https://example.com/api/data")
            .Returns(() => Task.FromResult(responses.Dequeue()));
        using var client = CreateClientWithHandler(handler);
        var response = await client.GetAsync(new Uri("https://example.com/api/data"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.VerifyRequest(HttpMethod.Get, "https://example.com/api/data", Times.Exactly(2));
    }

    [Fact]
    public async Task SendAsync_ShouldRetryOnTooManyRequests_429()
    {
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, "https://example.com/api/data")
            .Returns(() => Task.FromResult(responses.Dequeue()));
        using var client = CreateClientWithHandler(handler);
        var response = await client.GetAsync(new Uri("https://example.com/api/data"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.VerifyRequest(HttpMethod.Get, "https://example.com/api/data", Times.Exactly(2));
    }

    [Fact]
    public async Task SendAsync_ShouldRetryOnRequestTimeout_408()
    {
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.RequestTimeout));
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, "https://example.com/api/data")
            .Returns(() => Task.FromResult(responses.Dequeue()));
        using var client = CreateClientWithHandler(handler);
        var response = await client.GetAsync(new Uri("https://example.com/api/data"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.VerifyRequest(HttpMethod.Get, "https://example.com/api/data", Times.Exactly(2));
    }

    [Fact]
    public async Task SendAsync_ShouldNotRetryOnBadRequest_400()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, "https://example.com/api/data").ReturnsResponse(HttpStatusCode.BadRequest);
        using var client = CreateClientWithHandler(handler);
        var response = await client.GetAsync(new Uri("https://example.com/api/data"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        handler.VerifyRequest(HttpMethod.Get, "https://example.com/api/data", Times.Once());
    }

    [Fact]
    public async Task SendAsync_ShouldNotRetryOnNotFound_404()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, "https://example.com/api/data").ReturnsResponse(HttpStatusCode.NotFound);
        using var client = CreateClientWithHandler(handler);
        var response = await client.GetAsync(new Uri("https://example.com/api/data"));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        handler.VerifyRequest(HttpMethod.Get, "https://example.com/api/data", Times.Once());
    }

    [Fact]
    public async Task SendAsync_ShouldNotRetryOnUnauthorized_401()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, "https://example.com/api/data").ReturnsResponse(HttpStatusCode.Unauthorized);
        using var client = CreateClientWithHandler(handler);
        var response = await client.GetAsync(new Uri("https://example.com/api/data"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        handler.VerifyRequest(HttpMethod.Get, "https://example.com/api/data", Times.Once());
    }

    // ─── Circuit breaker ────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_ShouldOpenCircuitBreaker_AfterFiveConsecutiveFailures()
    {
        // The combined policy = retry(3) wraps circuit-breaker(5).
        // Each retry attempt passes through the circuit breaker.
        // Making calls that always return 503 eventually opens the breaker:
        //   Call 1: 4 CB attempts (1 + 3 retries), all fail → retry returns last 503.
        //   Call 2: 1st attempt → 5th CB failure → CB opens.
        //           Due to Polly's internal counting, the 5th failure transitions
        //           the breaker to Open. The next attempt through CB throws immediately.
        //   Result: one of the early attempts on call 2 or 3 will throw BrokenCircuitException.
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, "https://example.com/api/data")
            .ReturnsResponse(HttpStatusCode.ServiceUnavailable);

        using var client = CreateClientWithHandler(handler);

        // Exhaust retries on call 1 (returns 503, not an exception).
        var resp1 = await client.GetAsync(new Uri("https://example.com/api/data"));
        resp1.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        // Call 2: some attempts trip the breaker; eventually an exception is thrown.
        // The exact number of calls depends on Polly's internal counting granularity,
        // but after enough consecutive failures the circuit opens.
        var act = () => client.GetAsync(new Uri("https://example.com/api/data"));
        await act.Should().ThrowAsync<BrokenCircuitException>("circuit breaker should open after repeated failures");
    }

    // ─── TaskCanceledException triggers retry ──────────────────────

    [Fact]
    public async Task SendAsync_ShouldRetryOnTaskCanceledException()
    {
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("recovered") });
        var callCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, "https://example.com/api/data")
            .Returns(() =>
            {
                callCount++;
                if (callCount <= 2)
                    throw new TaskCanceledException("Simulated timeout");
                return Task.FromResult(responses.Dequeue());
            });

        using var client = CreateClientWithHandler(handler);
        var response = await client.GetAsync(new Uri("https://example.com/api/data"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("recovered");
        handler.VerifyRequest(HttpMethod.Get, "https://example.com/api/data", Times.Exactly(3));
    }

    // ─── TaskCanceledException exhausts retries ─────────────────────

    [Fact]
    public async Task SendAsync_ShouldThrowTaskCanceledException_WhenAllRetriesExhausted()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, "https://example.com/api/data")
            .ThrowsAsync(new TaskCanceledException("Simulated timeout"));

        using var client = CreateClientWithHandler(handler);

        var act = () => client.GetAsync(new Uri("https://example.com/api/data"));
        await act.Should().ThrowAsync<TaskCanceledException>();
    }
}
