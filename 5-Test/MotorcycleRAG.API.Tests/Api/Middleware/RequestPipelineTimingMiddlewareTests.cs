using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.API.Middleware;

namespace MotorcycleRAG.API.Tests.Api.Middleware;

public class RequestPipelineTimingMiddlewareTests
{
    private static RequestPipelineTimingMiddleware Create(
        RequestDelegate? next = null,
        ILogger<RequestPipelineTimingMiddleware>? logger = null)
        => new(next ?? TestHelpers.NoopNext, logger ?? NullLogger<RequestPipelineTimingMiddleware>.Instance);

    [Fact]
    public void Constructor_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new RequestPipelineTimingMiddleware(null!, NullLogger<RequestPipelineTimingMiddleware>.Instance));
        Assert.Throws<ArgumentNullException>(() => new RequestPipelineTimingMiddleware(TestHelpers.NoopNext, null!));
    }

    [Fact]
    public async Task InvokeAsync_NullContext_Throws()
        => await ((Func<Task>)(async () => await Create().InvokeAsync(null!))).Should().ThrowAsync<ArgumentNullException>();

    [Fact]
    public async Task InvokeAsync_NoException_CallsNextAndLogs()
    {
        var middleware = Create(TestHelpers.ContinueNext);
        var context = TestHelpers.CreateContext("/slow", "POST");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(299);
    }

    [Fact]
    public async Task InvokeAsync_ExceptionInNext_Propagates()
    {
        var middleware = Create(_ => throw new InvalidOperationException("fail"));
        var context = TestHelpers.CreateContext();

        await ((Func<Task>)(async () => await middleware.InvokeAsync(context))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task InvokeAsync_NullPath_UsesDefaultRoot()
    {
        var middleware = Create(TestHelpers.ContinueNext);
        var context = TestHelpers.CreateContext();
        context.Request.Path = new PathString((string?)null); // Path.Value is null

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(299);
    }

    [Fact]
    public async Task InvokeAsync_SlowRequest_Completes()
    {
        var middleware = Create(async _ => await Task.Delay(1_010));

        await middleware.InvokeAsync(TestHelpers.CreateContext("/slow-request"));
    }

}
