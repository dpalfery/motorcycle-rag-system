#pragma warning disable CA1506 // Avoid excessive class coupling
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using MotorcycleRag.WebUI.BFF.Middleware;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// CORS - Allow requests from the frontend SPA
builder.Services.AddCors(options => {
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins")?.Get<string[]>() ?? ["http://localhost:3000"];
    options.AddPolicy("AllowFrontend", policy => {
        policy
            .WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PUT", "DELETE")  // Explicit methods only
            .WithHeaders("Content-Type", "Authorization", "X-Requested-With")  // Explicit headers
            .AllowCredentials()
            .WithExposedHeaders("Content-Disposition")  // Allow download headers
            .SetPreflightMaxAge(TimeSpan.FromMinutes(5));  // Cache preflight for 5 minutes
    });
});

// YARP
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(builderContext => {
        // Attach Bearer Token from User Identity to downstream requests
        builderContext.AddRequestTransform(async transformContext => {
            var token = await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions
                .GetTokenAsync(transformContext.HttpContext, "access_token")
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token)) {
                transformContext.ProxyRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
        });
    });

// Authentication
builder.Services.AddAuthentication(options => {
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(options => {
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Name = "__Host-MotorcycleRAG";
    options.Cookie.Path = "/";
    options.Cookie.IsEssential = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(1);
    options.SlidingExpiration = true;
    options.Cookie.MaxAge = TimeSpan.FromHours(1);
    options.Cookie.Domain = null; // Prevent subdomain attacks
})
.AddOpenIdConnect(options => {
    var authConfig = builder.Configuration.GetSection("AzureAd");
    options.Authority = $"{authConfig["Instance"]}{authConfig["TenantId"]}";
    options.ClientId = authConfig["ClientId"];
    options.ClientSecret = Environment.GetEnvironmentVariable("MCR_BFF_CLIENT_SECRET")
        ?? throw new InvalidOperationException(
            "MCR_BFF_CLIENT_SECRET environment variable is required but not set. " +
            "Please set this to your BFF app registration client secret. " +
            "For local development, use: dotnet user-secrets set \"MCR_BFF_CLIENT_SECRET\" \"your-secret\"");
    options.ResponseType = "code";
    options.SaveTokens = true;
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("api");

    // Redirect hardening
    options.ProtocolValidator.RequireNonce = true;
    options.ProtocolValidator.RequireState = true;
    options.ProtocolValidator.RequireStateValidation = true;

    // PKCE protection
    options.UsePkce = true;
    options.ResponseMode = "query";

    // Token validation
    options.TokenValidationParameters.ValidateIssuer = true;
    options.TokenValidationParameters.ValidateAudience = true;
    options.TokenValidationParameters.ValidateLifetime = true;
    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
    options.TokenValidationParameters.RequireExpirationTime = true;
    options.TokenValidationParameters.RequireSignedTokens = true;
});

var app = builder.Build();

// Pipeline - Security-first approach
// 1. HTTPS enforcement
app.UseHttpsRedirection();

// 2. HSTS (HTTP Strict Transport Security) - force HTTPS for 1 year
app.UseHsts();

// 3. Host Header Validation MUST be early to prevent injection attacks
app.UseHostHeaderValidation();

// 4. Security Headers
app.Use(async (context, next) => {
    // Prevent clickjacking attacks
    context.Response.Headers.Append("X-Frame-Options", "DENY");

    // Prevent MIME type sniffing
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");

    // Enable XSS protection
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");

    // Content Security Policy - environment-dependent strictness
    var cspPolicy = app.Environment.IsDevelopment()
        ? "default-src 'self'; " +
          "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " + // Development: allow inline scripts for React HMR
          "style-src 'self' 'unsafe-inline'; " + // Development: allow inline styles for Pigment CSS development
          "img-src 'self' data: https:; " +
          "font-src 'self' data:; " +
          "connect-src 'self' ws: wss:; " + // Allow WebSocket for HMR
          "frame-ancestors 'none'; " +
          "base-uri 'self'; " +
          "form-action 'self';"
        : "default-src 'none'; " +
          "script-src 'self'; " + // Production: external scripts only
          "style-src 'self'; " + // Production: Pigment CSS outputs to external files
          "img-src 'self' data: https:; " +
          "font-src 'self' data:; " +
          "connect-src 'self'; " +
          "frame-ancestors 'none'; " +
          "base-uri 'self'; " +
          "form-action 'self'; " +
          "upgrade-insecure-requests;"; // Redirect HTTP to HTTPS

    context.Response.Headers.Append("Content-Security-Policy", cspPolicy);

    // Referrer Policy - control referrer information
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

    // Permissions Policy - restrict browser features
    context.Response.Headers.Append("Permissions-Policy",
        "accelerometer=(), " +
        "ambient-light-sensor=(), " +
        "autoplay=(), " +
        "battery=(), " +
        "camera=(), " +
        "cross-origin-isolated=(), " +
        "display-capture=(), " +
        "document-domain=(), " +
        "encrypted-media=(), " +
        "execution-while-not-rendered=(), " +
        "execution-while-out-of-viewport=(), " +
        "fullscreen=(), " +
        "geolocation=(), " +
        "gyroscope=(), " +
        "magnetometer=(), " +
        "microphone=(), " +
        "midi=(), " +
        "navigation-override=(), " +
        "payment=(), " +
        "picture-in-picture=(), " +
        "publickey-credentials-get=(), " +
        "speaker-selection=(), " +
        "sync-xhr=(), " +
        "usb=(), " +
        "vr=(), " +
        "xr-spatial-tracking=()");

    await next().ConfigureAwait(false);
});

// 5. Static files
app.UseStaticFiles();

// 6. Routing (must come before CORS)
app.UseRouting();

// 7. CORS - must be after routing but before authentication
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// YARP Endpoints
app.MapReverseProxy();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "MotorcycleRag.WebUI.BFF" }));

// Fallback to React (SPA)
app.MapFallbackToFile("index.html");
#pragma warning restore CA1506 // Avoid excessive class coupling
