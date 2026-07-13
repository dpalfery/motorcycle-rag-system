using System.Net;
using Moq.Contrib.HttpClient;
using MotorcycleRAG.Persistence.Azure;

namespace MotorcycleRAG.Persistence.Tests.Azure;
public class HttpResilienceDelegatingHandlerTests
{
    private static HttpClient CreateClientWithHandler(Mock<HttpMessageHandler> handler)
    {
        return new HttpClient(new HttpResilienceDelegatingHandler { InnerHandler = handler.Object });
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
    }

    [Fact]
    public async Task SendAsync_ShouldNotRetryOnBadRequest_400()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, "https://example.com/api/data").ReturnsResponse(HttpStatusCode.BadRequest);
        using var client = CreateClientWithHandler(handler);
        var response = await client.GetAsync(new Uri("https://example.com/api/data"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SendAsync_ShouldNotRetryOnNotFound_404()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, "https://example.com/api/data").ReturnsResponse(HttpStatusCode.NotFound);
        using var client = CreateClientWithHandler(handler);
        var response = await client.GetAsync(new Uri("https://example.com/api/data"));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SendAsync_ShouldNotRetryOnUnauthorized_401()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, "https://example.com/api/data").ReturnsResponse(HttpStatusCode.Unauthorized);
        using var client = CreateClientWithHandler(handler);
        var response = await client.GetAsync(new Uri("https://example.com/api/data"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
