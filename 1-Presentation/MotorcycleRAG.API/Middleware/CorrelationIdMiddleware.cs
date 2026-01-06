using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.API.Middleware
{
    /// <summary>
    /// Middleware for adding correlation IDs to requests and response headers
    /// </summary>
    internal class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;
        private const string CorrelationIdHeader = "X-Correlation-ID";

        /// <summary>
        /// Initializes a new instance of the CorrelationIdMiddleware
        /// </summary>
        /// <param name="next">Next middleware in the pipeline</param>
        /// <param name="logger">Logger</param>
        internal CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Invokes the middleware
        /// </summary>
        /// <param name="context">HTTP context</param>
        /// <returns>Task</returns>
        internal async Task InvokeAsync(HttpContext context)
        {
            // Check if correlation ID already exists in request headers
            if (!context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationId))
            {
                // Generate new correlation ID if not provided
                correlationId = Guid.NewGuid().ToString();
                _logger.LogDebug("Generated new correlation ID: {CorrelationId}", (object)correlationId);
            }
            else
            {
                var correlationIdString = correlationId.ToString();
                _logger.LogDebug("Using existing correlation ID: {CorrelationId}", correlationIdString ?? "null");
            }

            // Set correlation ID in response headers
            context.Response.Headers[CorrelationIdHeader] = correlationId;

            // Set correlation ID in trace identifier for logging
            context.TraceIdentifier = correlationId.ToString();

            // Add correlation ID to logging scope
            using (_logger.BeginScope(new { CorrelationId = correlationId }))
            {
                try
                {
                    await _next(context);
                }
                catch (Exception ex)
                {
                    var correlationIdString = correlationId.ToString();
                    _logger.LogError(ex, "Request failed with correlation ID {CorrelationId}", correlationIdString ?? "null");
                    throw new InvalidOperationException($"Request pipeline failed (CorrelationId: {correlationIdString})", ex);
                }
            }
        }
    }

    /// <summary>
    /// Extension method for adding the correlation ID middleware to the pipeline
    /// </summary>
    internal static class CorrelationIdMiddlewareExtensions
    {
        /// <summary>
        /// Adds the correlation ID middleware to the pipeline
        /// </summary>
        /// <param name="builder">Web application builder</param>
        /// <returns>Web application builder</returns>
        internal static IApplicationBuilder UseCorrelationId(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CorrelationIdMiddleware>();
        }
    }
}