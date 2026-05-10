namespace MotorcycleRag.WebUI.BFF.Extensions;

/// <summary>
/// Extension methods for <see cref="IApplicationBuilder"/> to add security headers.
/// </summary>
internal static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, IWebHostEnvironment env)
    {
        return app.Use(async (context, next) =>
        {
            // Prevent clickjacking attacks
            context.Response.Headers.Append("X-Frame-Options", "DENY");

            // Prevent MIME type sniffing
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");

            // Enable XSS protection
            context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");

            // Content Security Policy
            var cspPolicy = env.IsDevelopment()
                ? "default-src 'self'; " +
                  "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
                  "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
                  "img-src 'self' data: https:; " +
                  "font-src 'self' data: https://fonts.gstatic.com; " +
                  "connect-src 'self' ws: wss:; " +
                  "frame-ancestors 'none'; " +
                  "base-uri 'self'; " +
                  "form-action 'self';"
                : "default-src 'none'; " +
                  "script-src 'self'; " +
                  "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
                  "img-src 'self' data: https:; " +
                  "font-src 'self' data: https://fonts.gstatic.com; " +
                  "connect-src 'self'; " +
                  "frame-ancestors 'none'; " +
                  "base-uri 'self'; " +
                  "form-action 'self'; " +
                  "upgrade-insecure-requests;";

            context.Response.Headers.Append("Content-Security-Policy", cspPolicy);

            // Referrer Policy
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

            // Permissions Policy
            context.Response.Headers.Append("Permissions-Policy",
                "accelerometer=(), " +
                "ambient-light-sensor=(), " +
                "autoplay=(), " +
                "camera=(), " +
                "cross-origin-isolated=(), " +
                "display-capture=(), " +
                "encrypted-media=(), " +
                "fullscreen=(), " +
                "geolocation=(), " +
                "gyroscope=(), " +
                "magnetometer=(), " +
                "microphone=(), " +
                "midi=(), " +
                "payment=(), " +
                "picture-in-picture=(), " +
                "publickey-credentials-get=(), " +
                "sync-xhr=(), " +
                "unload=(), " +
                "usb=(), " +
                "xr-spatial-tracking=()");

            await next().ConfigureAwait(false);
        });
    }
}
