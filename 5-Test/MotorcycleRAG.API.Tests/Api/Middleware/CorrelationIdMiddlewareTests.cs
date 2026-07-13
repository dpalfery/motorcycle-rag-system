using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.API.Middleware;

namespace MotorcycleRAG.API.Tests.Api.Middleware;

public class CorrelationIdMiddlewareTests
{
    private static CorrelationIdMiddleware Create(RequestDelegate? next = null)
        => new(next ?? TestHelpers.NoopNext, NullLogger<CorrelationIdMiddleware>.Instance);

    [Fact]
    public void Constructor_NullArgs_Throw()
    {
        ((Action)(() => new CorrelationIdMiddleware(null!, NullLogger<CorrelationIdMiddleware>.Instance))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new CorrelationIdMiddleware(TestHelpers.NoopNext, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_NullContext_Throws()
        => await ((Func<Task>)(async () => await Create().InvokeAsync(null!))).Should().ThrowAsync<ArgumentNullException>();

    [Fact]
    public async Task InvokeAsync_NoCorrelationHeader_GeneratesOneAndSetsOnResponse()
    {
        var middleware = Create(TestHelpers.ContinueNext);
        var context = TestHelpers.CreateContext();
        context.Request.Headers.Remove("X-Correlation-ID");

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Correlation-ID"].ToString().Should().NotBeNullOrEmpty();
        context.TraceIdentifier.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ExistingCorrelationHeader_IsPreserved()
    {
        var middleware = Create(TestHelpers.ContinueNext);
        var context = TestHelpers.CreateContext();
        context.Request.Headers["X-Correlation-ID"] = "existing-id";

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Correlation-ID"].ToString().Should().Be("existing-id");
        context.TraceIdentifier.Should().Be("existing-id");
    }

    [Fact]
    public async Task InvokeAsync_Exception_Rethrows()
    {
        var middleware = Create(_ => throw new InvalidOperationException("boom"));
        var context = TestHelpers.CreateContext();

        await ((Func<Task>)(async () => await middleware.InvokeAsync(context))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task InvokeAsync_OperationCanceled_Rethrows()
    {
        var middleware = Create(_ => throw new OperationCanceledException());
        var context = TestHelpers.CreateContext();

        await ((Func<Task>)(async () => await middleware.InvokeAsync(context))).Should().ThrowAsync<OperationCanceledException>();
    }
}
