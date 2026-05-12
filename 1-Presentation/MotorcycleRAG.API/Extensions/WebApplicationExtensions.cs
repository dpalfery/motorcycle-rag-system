using MotorcycleRAG.API.Middleware;
using MotorcycleRAG.API.Services;
using MotorcycleRAG.API.Configuration;

namespace MotorcycleRAG.API.Extensions;

/// <summary>
/// Extension methods for <see cref="WebApplication"/> to configure the HTTP request pipeline.
/// </summary>
internal static class WebApplicationExtensions
{
    /// <summary>
    /// Configures the Motorcycle RAG API middleware pipeline in the correct security-critical order.
    /// </summary>
    public static WebApplication UseMotorcycleRagMiddleware(this WebApplication app)
    {
        // Configure the HTTP request pipeline
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Motorcycle RAG API v1");
                c.RoutePrefix = string.Empty; // Serve Swagger UI at root
            });
        }

        // Enable automatic refresh of configuration values from Azure App Configuration
        if (bool.TryParse(app.Configuration[AppConfigurationExtensions.AppConfigurationEnabledKey], out var appConfigEnabled) &&
            appConfigEnabled)
        {
            app.UseAzureAppConfiguration();
        }

        // Middleware order is critical for security:
        // 1. HTTPS redirection (enforce secure transport)
        // 2. Host header validation (OWASP A07:2021 - prevent Host Header Injection)
        // 3. Security headers (defense-in-depth)
        // 4. Correlation tracking (observability)
        // 5. Rate limiting (DOS/CSRF prevention - MUST be before CORS to prevent bypass)
        // 6. Exception handling (graceful error responses)
        // 7. CORS (restricted cross-origin access)
        // 8. Authentication (identity verification)
        // 9. Authorization (access control)

        app.UseHttpsRedirection();
        app.UseHostHeaderValidation(); // CRITICAL: Prevent Host Header Injection attacks
        app.UseSecurityHeaders();
        app.UseCorrelationId();
        app.UseRateLimiter(); // CRITICAL: Before CORS to prevent preflight bypass
        app.UseExceptionHandling();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorizationLogging(); // Add authorization logging middleware
        app.UseAuthorization();

        // Map controllers and health checks with rate limiting policies applied
        app.MapControllers().RequireRateLimiting("authenticated");

        // Map global health check endpoint
        app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            ResponseWriter = HealthCheckResponseWriter.WriteResponse,
            AllowCachingResponses = false
        }).AllowAnonymous().RequireRateLimiting("public");

        return app;
    }

    /// <summary>
    /// Pre-warms the JWT signing key cache to avoid blocking on the first request.
    /// </summary>
    public static async Task PreWarmJwtSigningKeysAsync(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
        
        try
        {
            var authConfig = app.Configuration.GetSection("Authentication:Issuers");
            var workforceIssuer = authConfig["Workforce"];
            var externalIdIssuer = authConfig["ExternalId"];

            if (!string.IsNullOrEmpty(workforceIssuer))
            {
                var signingKeyCache = app.Services.GetRequiredService<SigningKeyCache>();
                logger.LogInformation("Pre-warming JWT signing key cache...");
                await signingKeyCache.PreWarmCacheAsync(workforceIssuer, externalIdIssuer);
                logger.LogInformation("JWT signing key cache pre-warming completed");
            }
            else
            {
                logger.LogWarning("Workforce issuer not configured - signing key cache will not be pre-warmed");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error pre-warming JWT signing key cache. Application will continue but JWT validation may fail on first request.");
        }
    }
}
