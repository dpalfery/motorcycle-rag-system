using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace MotorcycleRAG.API.Middleware;

/// <summary>
/// Middleware for comprehensive authorization logging and auditing
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Middleware must be public for pipeline registration.")]
public sealed class AuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthorizationMiddleware> _logger;

    public AuthorizationMiddleware(
        RequestDelegate next,
        ILogger<AuthorizationMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Get correlation ID for tracking
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? "unknown";

        // Log authorization attempt
        var userId = context.User.Identity?.IsAuthenticated ?? false
            ? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous"
            : "anonymous";

        var userRoles = context.User.Identity?.IsAuthenticated ?? false
            ? string.Join(", ", context.User.FindAll(ClaimTypes.Role).Select(c => c.Value))
            : "none";

        var rawRoleClaims = context.User.Identity?.IsAuthenticated ?? false
            ? string.Join(", ", context.User.FindAll("roles").Select(c => c.Value))
            : "none";

        var scopeClaims = context.User.Identity?.IsAuthenticated ?? false
            ? string.Join(", ", context.User.FindAll("scp").Select(c => c.Value))
            : "none";

        var altScopeClaims = context.User.Identity?.IsAuthenticated ?? false
            ? string.Join(", ", context.User.FindAll("http://schemas.microsoft.com/identity/claims/scope").Select(c => c.Value))
            : "none";

        var azp = context.User.Identity?.IsAuthenticated ?? false
            ? context.User.FindFirst("azp")?.Value ?? "missing"
            : "none";

        _logger.LogInformation(
            "Authorization attempt - CorrelationId: {CorrelationId}, UserId: {UserId}, Roles: {Roles}, RolesClaim: {RolesClaim}, Scopes: {Scopes}, AltScopes: {AltScopes}, Azp: {Azp}, Path: {Path}, Method: {Method}",
            correlationId,
            userId,
            userRoles,
            rawRoleClaims,
            scopeClaims,
            altScopeClaims,
            azp,
            context.Request.Path,
            context.Request.Method);

        // Check if endpoint has authorization requirements
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata?.GetMetadata<IAuthorizeData>() != null)
        {
            _logger.LogDebug(
                "Endpoint requires authorization - CorrelationId: {CorrelationId}, Path: {Path}",
                correlationId,
                context.Request.Path);

            // Check if user is authenticated
            if (!context.User.Identity?.IsAuthenticated ?? false)
            {
                _logger.LogWarning(
                    "Unauthorized access attempt - CorrelationId: {CorrelationId}, UserId: {UserId}, Path: {Path}",
                    correlationId,
                    userId,
                    context.Request.Path);
            }
        }

        // Continue to next middleware
        await _next(context);

        // Log authorization result
        var statusCode = context.Response.StatusCode;
        if (statusCode == StatusCodes.Status401Unauthorized)
        {
            _logger.LogWarning(
                "Authorization failed - Unauthorized - CorrelationId: {CorrelationId}, UserId: {UserId}, Roles: {Roles}, RolesClaim: {RolesClaim}, Scopes: {Scopes}, AltScopes: {AltScopes}, Path: {Path}",
                correlationId,
                userId,
                userRoles,
                rawRoleClaims,
                scopeClaims,
                altScopeClaims,
                context.Request.Path);
        }
        else if (statusCode == StatusCodes.Status403Forbidden)
        {
            _logger.LogWarning(
                "Authorization failed - Forbidden - CorrelationId: {CorrelationId}, UserId: {UserId}, Roles: {Roles}, RolesClaim: {RolesClaim}, Scopes: {Scopes}, AltScopes: {AltScopes}, Azp: {Azp}, Path: {Path}",
                correlationId,
                userId,
                userRoles,
                rawRoleClaims,
                scopeClaims,
                altScopeClaims,
                azp,
                context.Request.Path);
        }
        else if (statusCode is >= 200 and < 300)
        {
            _logger.LogInformation(
                "Authorization successful - CorrelationId: {CorrelationId}, UserId: {UserId}, Path: {Path}, Status: {StatusCode}",
                correlationId,
                userId,
                context.Request.Path,
                statusCode);
        }
    }
}

/// <summary>
/// Extension methods for AuthorizationMiddleware
/// </summary>
internal static class AuthorizationMiddlewareExtensions
{
    internal static IApplicationBuilder UseAuthorizationLogging(this IApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseMiddleware<AuthorizationMiddleware>();
    }
}
