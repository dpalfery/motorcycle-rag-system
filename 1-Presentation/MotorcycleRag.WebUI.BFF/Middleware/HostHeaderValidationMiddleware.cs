using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace MotorcycleRag.WebUI.BFF.Middleware;

/// <summary>
/// Middleware for validating Host headers against a configured allowlist.
/// Prevents Host Header Injection attacks (OWASP A07:2021 - Cross-Site Request Forgery).
/// </summary>
#pragma warning disable CA1812 // Instantiated by ASP.NET Core middleware pipeline via reflection
#pragma warning disable S3059 // Public constructor required by ASP.NET Core ActivatorUtilities for UseMiddleware<T>()
internal sealed class HostHeaderValidationMiddleware {
#pragma warning restore S3059
#pragma warning restore CA1812
    private readonly RequestDelegate _next;
    private readonly ILogger<HostHeaderValidationMiddleware> _logger;
    private readonly HashSet<string> _allowedHosts;
    private readonly bool _allowAll;
#pragma warning disable S4055 // Log messages are inline strings; ResourceManager would be overkill for middleware
    private const string ErrorMessage =
        "HostHeaderValidationMiddleware configuration error: AllowedHosts is empty. " +
        "Configure 'AllowedHosts' in appsettings.json with a semicolon or comma-separated list of allowed hostnames. " +
        "Example: 'AllowedHosts': 'localhost;127.0.0.1;::1;ui.example.com;ui-staging.example.com'";

    /// <summary>
    /// Initializes a new instance of the HostHeaderValidationMiddleware
    /// </summary>
    /// <param name="next">Next middleware in the pipeline</param>
    /// <param name="logger">Logger</param>
    /// <param name="configuration">Application configuration</param>
    public HostHeaderValidationMiddleware(
        RequestDelegate next,
        ILogger<HostHeaderValidationMiddleware> logger,
        IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);

        _next = next;
        _logger = logger;

        // Parse AllowedHosts from configuration
        var allowedHostsConfig = configuration["AllowedHosts"] ?? "localhost";
        _allowAll = allowedHostsConfig.Trim() == "*";
        _allowedHosts = _allowAll
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                allowedHostsConfig
                    .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(h => h.Trim().ToUpperInvariant()),
                StringComparer.OrdinalIgnoreCase
            );

        // Fail fast if AllowedHosts is empty - this indicates a misconfiguration
        // (Skip the check when _allowAll is true - empty HashSet is intentional in that case)
        if (!_allowAll && _allowedHosts.Count == 0)
        {
#pragma warning disable CA1848 // Log message is a constant string, not expensive
            _logger.LogError(ErrorMessage);
#pragma warning restore CA1848
            throw new InvalidOperationException(ErrorMessage);
        }

#pragma warning disable CA1848 // Log message is a constant string, not expensive
#pragma warning disable CA1873 // Evaluation of this argument may be expensive and unnecessary if logging is disabled
        _logger.LogInformation(
            "HostHeaderValidationMiddleware initialized with allowed hosts: {AllowedHosts}",
            SanitizeLogValue(string.Join(", ", _allowedHosts), 500)
        );
#pragma warning restore CA1873
#pragma warning restore CA1848
    }

    /// <summary>
    /// Invokes the middleware to validate the Host header
    /// </summary>
    /// <param name="context">HTTP context</param>
    /// <returns>Task</returns>
    public async Task InvokeAsync(HttpContext context) {
        ArgumentNullException.ThrowIfNull(context);

        // Health check endpoints must bypass Host header validation.
        // ACA liveness/readiness probes use internal IPs (e.g. 100.100.0.218) as Host,
        // which would otherwise be rejected and cause container restarts.
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
#pragma warning disable CA1848
            _logger.LogDebug("Skipping Host header validation for health check path: {Path}", SanitizeLogValue(context.Request.Path, 200));
#pragma warning restore CA1848
            await _next(context).ConfigureAwait(false);
            return;
        }
        // Get the Host header value
        if (!context.Request.Headers.TryGetValue("Host", out var hostHeader))
        {
            // REJECT: HTTP/1.1 (RFC 7230) requires Host header; HTTP/2 maps :authority to Host header
#pragma warning disable CA1848 // Log message is a constant string, not expensive
            _logger.LogWarning("Request received without required Host header - rejecting as malformed");
#pragma warning restore CA1848
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new {
                type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title = "Bad Request",
                status = StatusCodes.Status400BadRequest,
                detail = "Host header is required by HTTP protocol specification",
                instance = context.Request.Path
            };

            await context.Response.WriteAsJsonAsync(problemDetails).ConfigureAwait(false);
            return;
        }

        var hostValue = hostHeader.ToString();
        if (string.IsNullOrWhiteSpace(hostValue))
        {
            // REJECT: Empty Host header violates RFC 7230
#pragma warning disable CA1848 // Log message is a constant string, not expensive
            _logger.LogWarning("Request received with empty Host header - rejecting as malformed");
#pragma warning restore CA1848
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new {
                type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title = "Bad Request",
                status = StatusCodes.Status400BadRequest,
                detail = "Host header cannot be empty",
                instance = context.Request.Path
            };

            await context.Response.WriteAsJsonAsync(problemDetails).ConfigureAwait(false);
            return;
        }

        // Extract hostname without port for comparison
        // Host header format: "hostname" or "hostname:port"
        var hostOnly = ExtractHostname(hostValue);

        // Validate against allowed hosts (case-insensitive)
        if (!IsHostAllowed(hostOnly))
        {
#pragma warning disable CA1848 // Log message is a constant string, not expensive
#pragma warning disable CA1873 // Evaluation of this argument may be expensive and unnecessary if logging is disabled
            _logger.LogWarning(
                "Host header validation failed. Host: {Host}, HostOnly: {HostOnly}, AllowedHosts: {AllowedHosts}",
                SanitizeLogValue(hostValue, 200),
                SanitizeLogValue(hostOnly, 200),
                SanitizeLogValue(string.Join(", ", _allowedHosts), 500)
            );
#pragma warning restore CA1873
#pragma warning restore CA1848

            // Return 400 Bad Request with ProblemDetails response
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new {
                type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title = "Bad Request",
                status = StatusCodes.Status400BadRequest,
                detail = "Invalid Host header. The requested host is not allowed.",
                instance = context.Request.Path
            };

            await context.Response.WriteAsJsonAsync(problemDetails).ConfigureAwait(false);
            return;
        }

#pragma warning disable CA1848 // Log message is a constant string, not expensive
#pragma warning disable CA1873 // Evaluation of this argument may be expensive and unnecessary if logging is disabled
        _logger.LogDebug(
            "Host header validation successful. Host: {Host}, HostOnly: {HostOnly}",
            SanitizeLogValue(hostValue, 200),
            SanitizeLogValue(hostOnly, 200)
        );
#pragma warning restore CA1873
#pragma warning restore CA1848

        await _next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts the hostname from a Host header value (removes port if present).
    /// RFC 3986 format: IPv6 addresses are enclosed in brackets, e.g., [::1]:8080
    /// IPv4/hostname format: hostname or hostname:port
    /// </summary>
    /// <param name="hostValue">The Host header value</param>
    /// <returns>The hostname without port, or empty string if malformed</returns>
    private static string ExtractHostname(string hostValue) {
        if (string.IsNullOrWhiteSpace(hostValue)) {
            return string.Empty;
        }

        // Handle IPv6 addresses in brackets (RFC 3986)
        // Format: [address] or [address]:port
        // Example: [::1] or [2001:db8::1]:8080
        if (hostValue.StartsWith('[')) {
            var closingBracket = hostValue.IndexOf(']');

            // closingBracket returns -1 if not found, or the index position if found
            // We require closingBracket > 0 to ensure there's at least one character between [ and ]
            // (position 0 would mean empty brackets [], which is invalid)
            return closingBracket > 0
                ? hostValue.Substring(0, closingBracket + 1).ToUpperInvariant()
                : string.Empty;
        }

        // Handle IPv4 addresses and domain names
        // Look for port separator from the end (LastIndexOf to handle domain names with dots)
        var colonIndex = hostValue.LastIndexOf(':');
        if (colonIndex > 0) {
            // Verify that what follows the colon is actually a valid port number
            // This prevents misinterpreting part of the hostname as a port
            var potentialPort = hostValue.Substring(colonIndex + 1);
            if (int.TryParse(potentialPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return hostValue.Substring(0, colonIndex).ToUpperInvariant();
            }
        }

        // No port found, return the entire hostname
        return hostValue.ToUpperInvariant();
    }

    /// <summary>
    /// Checks if a given hostname is in the allowed hosts list.
    /// Performs case-insensitive comparison.
    /// </summary>
    /// <param name="hostname">The hostname to validate</param>
    /// <returns>True if the hostname is allowed; otherwise false</returns>
    private bool IsHostAllowed(string hostname) {
        if (string.IsNullOrWhiteSpace(hostname)) {
            return false;
        }

        // '*' in AllowedHosts means allow all hosts (matches ASP.NET Core HostFilteringMiddleware semantics)
        if (_allowAll) {
            return true;
        }

        // HashSet uses StringComparer.OrdinalIgnoreCase, so Contains handles case-insensitive comparison
        return _allowedHosts.Contains(hostname);
    }

    private static string SanitizeLogValue(object? value, int maxLength = 200) {
        var text = value?.ToString();
        if (text is null) {
            return string.Empty;
        }

        var sanitized = text
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace('\t', ' ')
            .Replace('\0', ' ');

        return sanitized.Length > maxLength
            ? sanitized[..maxLength]
            : sanitized;
    }
}

#pragma warning disable CA1515 // Extension methods should be public for external use
#pragma warning disable S3059 // Public methods required for ASP.NET Core extension methods
/// <summary>
/// Extension method for adding the Host Header Validation middleware to the pipeline
/// </summary>
public static class HostHeaderValidationMiddlewareExtensions {
#pragma warning restore S3059
#pragma warning restore CA1515
    /// <summary>
    /// Adds the Host Header Validation middleware to the pipeline.
    /// This middleware should be applied early in the pipeline, after routing but before authorization.
    /// </summary>
    /// <param name="builder">Web application builder</param>
    /// <returns>Web application builder</returns>
    public static IApplicationBuilder UseHostHeaderValidation(this IApplicationBuilder builder) {
        return builder.UseMiddleware<HostHeaderValidationMiddleware>();
    }
}
