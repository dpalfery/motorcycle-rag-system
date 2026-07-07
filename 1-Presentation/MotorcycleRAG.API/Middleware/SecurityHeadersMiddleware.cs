using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace MotorcycleRAG.API.Middleware;

/// <summary>
/// Middleware for adding security headers to responses
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Middleware must be public for pipeline registration.")]
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityHeadersMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    /// <summary>
    /// Initializes a new instance of the SecurityHeadersMiddleware
    /// </summary>
    /// <param name="next">Next middleware in the pipeline</param>
    /// <param name="logger">Logger</param>
    /// <param name="environment">Host environment</param>
    public SecurityHeadersMiddleware(RequestDelegate next, ILogger<SecurityHeadersMiddleware> logger, IHostEnvironment environment)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <summary>
    /// Invokes the middleware
    /// </summary>
    /// <param name="context">HTTP context</param>
    /// <returns>Task</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Add security headers
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "DENY");
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");

        var cspPolicy = "default-src 'none'; " +
            "script-src 'none'; " +
            "style-src 'none'; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "connect-src 'self'; " +
            "frame-src 'none'; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'none'";

        context.Response.Headers.Append("Content-Security-Policy", cspPolicy);
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
        context.Response.Headers.Append("Permissions-Policy", "geolocation=(), microphone=(), camera=(), payment=(), usb=()");

        // HSTS must NOT be sent in Development. On localhost the "includeSubDomains" directive
        // poisons the entire localhost HSTS cache, force-upgrading http://localhost:* requests
        // (e.g. the Tauri/Vite dev server on :1420) to https and breaking them. Only emit it for
        // genuine HTTPS responses in non-Development environments.
        if (!_environment.IsDevelopment() && context.Request.IsHttps)
        {
            context.Response.Headers.Append("Strict-Transport-Security", "max-age=63072000; includeSubDomains; preload");
        }

        _logger.LogDebug("Added security headers to response");

        await _next(context);
    }
}

/// <summary>
/// Extension method for adding to security headers middleware to the pipeline
/// </summary>
internal static class SecurityHeadersMiddlewareExtensions
{
    /// <summary>
    /// Adds the security headers middleware to the pipeline
    /// </summary>
    /// <param name="builder">Web application builder</param>
    /// <returns>Web application builder</returns>
    internal static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
