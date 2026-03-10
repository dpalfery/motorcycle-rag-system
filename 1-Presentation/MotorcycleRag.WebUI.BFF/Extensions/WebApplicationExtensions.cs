using MotorcycleRag.WebUI.BFF.Middleware;

namespace MotorcycleRag.WebUI.BFF.Extensions;

/// <summary>
/// Extension methods for <see cref="WebApplication"/> to configure the BFF HTTP request pipeline.
/// </summary>
internal static class WebApplicationExtensions
{
    public static WebApplication UseMotorcycleRagBffMiddleware(this WebApplication app)
    {
        // 0. Forwarded Headers — MUST be first for correct scheme/host detection
        app.UseForwardedHeaders();

        // 1. HSTS (HTTP Strict Transport Security)
        app.UseHsts();

        // 2. Host Header Validation
        app.UseHostHeaderValidation();

        // 3. Security Headers
        app.UseSecurityHeaders(app.Environment);

        // 4. Static files
        app.UseStaticFiles();

        // 5. Routing
        app.UseRouting();

        // 6. CORS
        app.UseCors("AllowFrontend");

        // 7. Authentication & Authorization
        app.UseAuthentication();
        app.UseAuthorization();

        // 8. Endpoints
        app.MapControllers();
        app.MapReverseProxy();
        app.MapHealthChecks("/health");

        // 9. Fallback to React (SPA)
        app.MapFallbackToFile("index.html");

        return app;
    }
}
