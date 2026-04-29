namespace MotorcycleRag.WebUI.BFF.Configuration.Services;

/// <summary>
/// Configuration for CORS in the BFF.
/// </summary>
internal static class CorsServiceConfiguration {
    public static IServiceCollection AddBffCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment) {
        services.AddCors(options => {
            var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins")?.Get<string[]>() ?? ["http://localhost:3000"];
            var allowedOrigins = environment.IsDevelopment()
                ? configuredOrigins
                    .Concat([
                        "http://localhost:5173",
                        "http://localhost:5174",
                        "http://127.0.0.1:5173",
                        "https://localhost:5173",
                        "https://localhost:5174",
                        "https://127.0.0.1:5173"
                    ])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : configuredOrigins;

            options.AddPolicy("AllowFrontend", policy => {
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
