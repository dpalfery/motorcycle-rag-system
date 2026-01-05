using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.API.Middleware
{
    /// <summary>
    /// Middleware for adding security headers to responses
    /// </summary>
    internal class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SecurityHeadersMiddleware> _logger;

        /// <summary>
        /// Initializes a new instance of the SecurityHeadersMiddleware
        /// </summary>
        /// <param name="next">Next middleware in the pipeline</param>
        /// <param name="logger">Logger</param>
        public SecurityHeadersMiddleware(RequestDelegate next, ILogger<SecurityHeadersMiddleware> logger)
        {
            _next = next ?? throw new System.ArgumentNullException(nameof(next));
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Invokes the middleware
        /// </summary>
        /// <param name="context">HTTP context</param>
        /// <returns>Task</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            // Add security headers
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Append("X-Frame-Options", "DENY");
            context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
            var cspPolicy = "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data:; " +
                "font-src 'self'; " +
                "connect-src 'self'; " +
                "frame-src 'none'; " +
                "object-src 'none'; " +
                "base-uri 'self'; " +
                "form-action 'self'";
            context.Response.Headers.Append("Content-Security-Policy", cspPolicy);
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            context.Response.Headers.Append("Permissions-Policy", 
                "geolocation=(), microphone=(), camera=(), payment=(), usb=()");
            context.Response.Headers.Append("Strict-Transport-Security", "max-age=63072000; includeSubDomains; preload");

            _logger.LogDebug("Added security headers to response");

            await _next(context);
        }
    }

    /// <summary>
    /// Extension method for adding the security headers middleware to the pipeline
    /// </summary>
    internal static class SecurityHeadersMiddlewareExtensions
    {
        /// <summary>
        /// Adds the security headers middleware to the pipeline
        /// </summary>
        /// <param name="builder">Web application builder</param>
        /// <returns>Web application builder</returns>
        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SecurityHeadersMiddleware>();
        }
    }
}