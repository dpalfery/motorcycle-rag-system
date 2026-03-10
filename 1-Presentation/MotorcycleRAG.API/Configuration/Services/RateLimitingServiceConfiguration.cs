using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using MotorcycleRAG.API.Extensions;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for rate limiting services.
/// </summary>
internal static class RateLimitingServiceConfiguration
{
    public static IServiceCollection AddMotorcycleRagRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("public", rateLimiterOptions =>
            {
                rateLimiterOptions.Window = TimeSpan.FromSeconds(10);
                rateLimiterOptions.PermitLimit = 100;
                rateLimiterOptions.QueueLimit = 50;
            });

            // Role-based rate limiting per user (using 'oid' claim as partition key)
            options.AddPolicy("authenticated", context =>
            {
                var user = context.User;
                var limit = GetRateLimitForUser(user);
                var window = TimeSpan.FromHours(1);

                // Partition by user object ID (oid) to track individual usage
                // Fallback to "anonymous" if no oid found (shouldn't happen for authenticated)
                var partitionKey = user.FindFirst("oid")?.Value ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = window,
                    QueueLimit = 10,
                    AutoReplenishment = true
                });
            });

            // Admin-only ingestion endpoints: Admin-tier limits (effectively unlimited)
            options.AddPolicy("ingestion-jobs", context =>
            {
                var partitionKey = context.User.FindFirst("oid")?.Value ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100_000,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 10,
                    AutoReplenishment = true
                });
            });

            // Manual page viewing: Pro-tier limits (500/hour)
            options.AddPolicy("manuals-view", context =>
            {
                var partitionKey = context.User.FindFirst("oid")?.Value ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 500,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 10,
                    AutoReplenishment = true
                });
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return services;
    }

    /// <summary>
    /// Gets the hourly rate limit for a user based on their roles.
    /// </summary>
    /// <param name="user">The claims principal.</param>
    /// <returns>The number of permitted requests per hour.</returns>
    internal static int GetRateLimitForUser(ClaimsPrincipal user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return 50;
        }

        // Roadrunner / mcr-api-admin: Unlimited
        if (user.HasAnyRole("Roadrunner", "mcr-api-admin"))
        {
            return 100000; // Effectively unlimited for practical purposes
        }
        
        // ProUser: 500/hour
        if (user.HasAnyRole("ProUser"))
        {
            return 500;
        }
        
        // DemoUser / Default: 50/hour
        return 50;
    }
}
