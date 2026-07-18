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

    [Fact]
    public async Task InvokeAsync_ControlCharactersAndOverlongPath_LogsReversibleStructuredFields()
    {
        const string attackerMethod = "P\\O\r\nS\tT";
        const string expectedEscapedMethod = "P\\\\O\\r\\nS\\tT";
        var attackerPath = $"/manual/{new string('x', 205)}\\tail\r\nentry\tend";
        var expectedEscapedPath = $"/manual/{new string('x', 205)}\\\\tail\\r\\nentry\\tend";
        var logger = new CapturingLogger<RequestPipelineTimingMiddleware>();
        var middleware = Create(TestHelpers.ContinueNext, logger);
        var context = TestHelpers.CreateContext();
        context.Request.Method = attackerMethod;
        context.Request.Path = attackerPath;

        await middleware.InvokeAsync(context);

        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.LogLevel.Should().Be(LogLevel.Debug);
        entry.Properties.Should().ContainKeys("Method", "Path", "ElapsedMs", "StatusCode", "{OriginalFormat}");
        entry.Properties["{OriginalFormat}"].Should().Be(
            "Request: {Method} {Path} completed in {ElapsedMs}ms (Status={StatusCode})");
        entry.Properties["Method"].Should().Be(expectedEscapedMethod);
        var sanitizedPath = entry.Properties["Path"].Should().BeOfType<string>().Which;
        sanitizedPath.Should().Be(expectedEscapedPath)
            .And.EndWith("\\\\tail\\r\\nentry\\tend");
        sanitizedPath.Length.Should().BeGreaterThan(200);
        entry.Message.Should().NotContain("\r")
            .And.NotContain("\n")
            .And.NotContain("\t");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<CapturedLogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

            if (state is IReadOnlyList<KeyValuePair<string, object?>> stateList)
            {
                foreach (var pair in stateList)
                {
                    properties[pair.Key] = pair.Value;
                }
            }

            Entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), properties));
        }

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record CapturedLogEntry(
        LogLevel LogLevel,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);
}
