using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MotorcycleRAG.API.Middleware;

/// <summary>
/// Middleware for validating Host headers against a configured allowlist.
/// Prevents Host Header Injection attacks (OWASP A07:2021 - Cross-Site Request Forgery).
/// </summary>
public class HostHeaderValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HostHeaderValidationMiddleware> _logger;
    private readonly HashSet<string> _allowedHosts;

    /// <summary>
    /// Initializes a new instance of the HostHeaderValidationMiddleware
    /// </summary>
    /// <param name="next">Next middleware in the pipeline</param>
    /// <param name="logger">Logger</param>
    /// <param name="configuration">Application configuration</param>
    public HostHeaderValidationMiddleware(
        RequestDelegate next,
        ILogger<HostHeaderValidationMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Parse AllowedHosts from configuration
        var allowedHostsConfig = configuration["AllowedHosts"] ?? "localhost";
        _allowedHosts = new HashSet<string>(
            allowedHostsConfig
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(h => h.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase
        );

        // Fail fast if AllowedHosts is empty - this indicates a misconfiguration
        if (_allowedHosts.Count == 0)
        {
            var errorMessage =
                "HostHeaderValidationMiddleware configuration error: AllowedHosts is empty. " +
                "Configure 'AllowedHosts' in appsettings.json with a comma-separated list of allowed hostnames. " +
                "Example: 'AllowedHosts': 'localhost,api.example.com,api-staging.example.com'";
            _logger.LogError(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        _logger.LogInformation(
            "HostHeaderValidationMiddleware initialized with allowed hosts: {AllowedHosts}",
            string.Join(", ", _allowedHosts)
        );
    }

    /// <summary>
    /// Invokes the middleware to validate the Host header
    /// </summary>
    /// <param name="context">HTTP context</param>
    /// <returns>Task</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        // Get the Host header value
        if (!context.Request.Headers.TryGetValue("Host", out var hostHeader))
        {
            // REJECT: HTTP/1.1 (RFC 7230) requires Host header; HTTP/2 maps :authority to Host header
            _logger.LogWarning("Request received without required Host header - rejecting as malformed");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title = "Bad Request",
                status = StatusCodes.Status400BadRequest,
                detail = "Host header is required by HTTP protocol specification",
                instance = context.Request.Path
            };

            await context.Response.WriteAsJsonAsync(problemDetails);
            return;
        }

        var hostValue = hostHeader.ToString();
        if (string.IsNullOrWhiteSpace(hostValue))
        {
            // REJECT: Empty Host header violates RFC 7230
            _logger.LogWarning("Request received with empty Host header - rejecting as malformed");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title = "Bad Request",
                status = StatusCodes.Status400BadRequest,
                detail = "Host header cannot be empty",
                instance = context.Request.Path
            };

            await context.Response.WriteAsJsonAsync(problemDetails);
            return;
        }

        // Extract hostname without port for comparison
        // Host header format: "hostname" or "hostname:port"
        var hostOnly = ExtractHostname(hostValue);

        // Validate against allowed hosts (case-insensitive)
        if (!IsHostAllowed(hostOnly))
        {
            _logger.LogWarning(
                "Host header validation failed. Host: {Host}, HostOnly: {HostOnly}, AllowedHosts: {AllowedHosts}",
                hostValue,
                hostOnly,
                string.Join(", ", _allowedHosts)
            );

            // Return 400 Bad Request with ProblemDetails response
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title = "Bad Request",
                status = StatusCodes.Status400BadRequest,
                detail = "Invalid Host header. The requested host is not allowed.",
                instance = context.Request.Path
            };

            await context.Response.WriteAsJsonAsync(problemDetails);
            return;
        }

        _logger.LogDebug(
            "Host header validation successful. Host: {Host}, HostOnly: {HostOnly}",
            hostValue,
            hostOnly
        );

        await _next(context);
    }

    /// <summary>
    /// Extracts the hostname from a Host header value (removes port if present).
    /// RFC 3986 format: IPv6 addresses are enclosed in brackets, e.g., [::1]:8080
    /// IPv4/hostname format: hostname or hostname:port
    /// </summary>
    /// <param name="hostValue">The Host header value</param>
    /// <returns>The hostname without port, or empty string if malformed</returns>
    private static string ExtractHostname(string hostValue)
    {
        if (string.IsNullOrWhiteSpace(hostValue))
        {
            return string.Empty;
        }

        // Handle IPv6 addresses in brackets (RFC 3986)
        // Format: [address] or [address]:port
        // Example: [::1] or [2001:db8::1]:8080
        if (hostValue.StartsWith("[", StringComparison.Ordinal))
        {
            var closingBracket = hostValue.IndexOf(']');

            // closingBracket returns -1 if not found, or the index position if found
            // We require closingBracket > 0 to ensure there's at least one character between [ and ]
            // (position 0 would mean empty brackets [], which is invalid)
            if (closingBracket > 0)
            {
                // Return the entire IPv6 address including brackets, excluding any port after ]
                return hostValue.Substring(0, closingBracket + 1).ToLowerInvariant();
            }
            else
            {
                // Malformed IPv6 address (missing closing bracket)
                return string.Empty;
            }
        }

        // Handle IPv4 addresses and domain names
        // Look for port separator from the end (LastIndexOf to handle domain names with dots)
        var colonIndex = hostValue.LastIndexOf(':');
        if (colonIndex > 0)
        {
            // Verify that what follows the colon is actually a valid port number
            // This prevents misinterpreting part of the hostname as a port
            var potentialPort = hostValue.Substring(colonIndex + 1);
            if (int.TryParse(potentialPort, out _))
            {
                return hostValue.Substring(0, colonIndex).ToLowerInvariant();
            }
        }

        // No port found, return the entire hostname
        return hostValue.ToLowerInvariant();
    }

    /// <summary>
    /// Checks if a given hostname is in the allowed hosts list.
    /// Performs case-insensitive comparison.
    /// </summary>
    /// <param name="hostname">The hostname to validate</param>
    /// <returns>True if the hostname is allowed; otherwise false</returns>
    private bool IsHostAllowed(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return false;
        }

        // HashSet uses StringComparer.OrdinalIgnoreCase, so Contains handles case-insensitive comparison
        return _allowedHosts.Contains(hostname);
    }
}

/// <summary>
/// Extension method for adding the Host Header Validation middleware to the pipeline
/// </summary>
public static class HostHeaderValidationMiddlewareExtensions
{
    /// <summary>
    /// Adds the Host Header Validation middleware to the pipeline.
    /// This middleware should be applied early in the pipeline, after routing but before authorization.
    /// </summary>
    /// <param name="builder">Web application builder</param>
    /// <returns>Web application builder</returns>
    public static IApplicationBuilder UseHostHeaderValidation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<HostHeaderValidationMiddleware>();
    }
}
