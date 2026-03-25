using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for Cross-Origin Resource Sharing (CORS).
/// </summary>
public static class CorsServiceConfiguration
{
    public static IServiceCollection AddRestrictedCors(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        // Configure CORS with strict security controls
        // OWASP A01:2021 - CSRF Risk Mitigation
        // Only allows explicitly configured origins and limits HTTP methods to necessary operations
        var corsOrigins = configuration["Cors:AllowedOrigins"]?.Split(";", StringSplitOptions.RemoveEmptyEntries)
            ?? new[] { "https://localhost:3000" }; // Default for local development only

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(corsOrigins)
                      .AllowCredentials() // Support HttpOnly cookies for secure auth
                      .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS") // Exclude PATCH, CONNECT, TRACE
                      .WithHeaders("Content-Type", "Authorization", "X-Requested-With") // Whitelist specific headers
                      .WithExposedHeaders("X-Total-Count") // Only expose necessary headers
                      .SetPreflightMaxAge(TimeSpan.FromSeconds(600)); // 10-minute preflight cache
            });
        });

        return services;
    }
}
