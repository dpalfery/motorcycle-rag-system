using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.API.Middleware;

/// <summary>
/// Measures total request pipeline duration and logs it as a structured metric.
/// Place early in the pipeline to capture auth + rate-limit + controller time.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "Middleware must be public for pipeline registration.")]
public sealed class RequestPipelineTimingMiddleware
{
    private const int SlowRequestThresholdMs = 1000;

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestPipelineTimingMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestPipelineTimingMiddleware"/>.
    /// </summary>
    /// <param name="next">Next middleware in the pipeline.</param>
    /// <param name="logger">Logger instance.</param>
    public RequestPipelineTimingMiddleware(
        RequestDelegate next,
        ILogger<RequestPipelineTimingMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Invokes the middleware, measuring total pipeline duration.
    /// </summary>
    /// <param name="context">HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();
        var path = context.Request.Path.Value ?? "/";

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            var statusCode = context.Response.StatusCode;

            // Log slow requests (> 1 second) at Warning level
            if (elapsedMs > SlowRequestThresholdMs)
            {
                _logger.LogWarning(
                    "Slow request: {Method} {Path} completed in {ElapsedMs}ms (Status={StatusCode})",
                    // codeql[cs/log-forging]
                    context.Request.Method,
                    path,
                    elapsedMs,
                    statusCode);
            }
            else
            {
                _logger.LogDebug(
                    "Request: {Method} {Path} completed in {ElapsedMs}ms (Status={StatusCode})",
                    // codeql[cs/log-forging]
                    context.Request.Method,
                    path,
                    elapsedMs,
                    statusCode);
            }
        }
    }
}

/// <summary>
/// Extension method for adding the request pipeline timing middleware to the pipeline.
/// </summary>
internal static class RequestPipelineTimingMiddlewareExtensions
{
    /// <summary>
    /// Adds the request pipeline timing middleware to the pipeline.
    /// </summary>
    /// <param name="builder">Application builder.</param>
    /// <returns>The application builder.</returns>
    internal static IApplicationBuilder UseRequestPipelineTiming(this IApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseMiddleware<RequestPipelineTimingMiddleware>();
    }
}
