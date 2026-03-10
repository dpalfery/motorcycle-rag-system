namespace MotorcycleRag.WebUI.BFF.Configuration.Services;

/// <summary>
/// Configuration for CORS in the BFF.
/// </summary>
internal static class CorsServiceConfiguration
{
    public static IServiceCollection AddBffCors(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins")?.Get<string[]>() ?? ["http://localhost:3000"];
            
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy
                    .WithOrigins(allowedOrigins)
                    .WithMethods("GET", "POST", "PUT", "DELETE") // Explicit methods only
                    .WithHeaders("Content-Type", "Authorization", "X-Requested-With") // Explicit headers
                    .AllowCredentials()
                    .WithExposedHeaders("Content-Disposition") // Allow download headers
                    .SetPreflightMaxAge(TimeSpan.FromMinutes(5)); // Cache preflight for 5 minutes
            });
        });

        return services;
    }
}
