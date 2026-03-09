#pragma warning disable CA1506 // Avoid excessive class coupling
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.DataContracts;
using MotorcycleRag.WebUI.BFF.Middleware;
using MotorcycleRag.WebUI.BFF.HealthChecks;
using MotorcycleRag.WebUI.BFF.Extensions;
using Yarp.ReverseProxy.Transforms;
using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Bootstrap TelemetryClient — MUST be initialised before App Config loads so that
// App Config failures, MSI errors, and other pre-DI startup exceptions are captured.
// ConnectionStrings__ApplicationInsights is injected directly as a container env var
// by Pulumi (not via App Config) for exactly this reason.
var bootstrapAiConnectionString =
    builder.Configuration.GetConnectionString("ApplicationInsights")
    ?? builder.Configuration["ApplicationInsights:ConnectionString"];

TelemetryClient? bootstrapTelemetry = null;
if (!string.IsNullOrEmpty(bootstrapAiConnectionString))
{
    var bootstrapTelemetryConfig = new TelemetryConfiguration { ConnectionString = bootstrapAiConnectionString };
    bootstrapTelemetry = new TelemetryClient(bootstrapTelemetryConfig);
}

// Add Azure App Configuration & Key Vault
var appConfigEndpoint = builder.Configuration["AppConfig:Endpoint"];
if (!string.IsNullOrEmpty(appConfigEndpoint)) {
    var credential = new DefaultAzureCredential();
    // Wait for external network egress to App Config to be ready.
    // On Container Apps cold starts, DNS/TCP to external services lags behind IMDS.
    // Test TCP before calling AddAzureAppConfiguration to avoid startup crash.
    var appConfigUri = new Uri(appConfigEndpoint);
    var appConfigHost = appConfigUri.Host;
    for (var attempt = 1; attempt <= 15; attempt++) {
        try {
            using var tcp = new System.Net.Sockets.TcpClient();
            var connectTask = tcp.ConnectAsync(appConfigHost, 443);
            if (await Task.WhenAny(connectTask, Task.Delay(5000)) == connectTask) {
                await connectTask;
                Console.WriteLine($"App Config TCP connectivity confirmed on attempt {attempt}.");
                break;
            }
            if (attempt < 15) {
                Console.WriteLine($"App Config TCP timed out (attempt {attempt}/15). Retrying in 2s...");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception ex) when (attempt < 15) {
            Console.WriteLine($"App Config TCP failed (attempt {attempt}/15): {ex.Message}. Retrying in 2s...");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
    builder.Configuration.AddAzureAppConfiguration(options => {
        options.Connect(new Uri(appConfigEndpoint), credential)
               // Load all non-labelled keys
               .Select(KeyFilter.Any)
               // Load BFF-specific labelled keys (overrides unlabelled keys for BFF)
               .Select(KeyFilter.Any, "bff")
               // Load environment-specific labelled keys (e.g. Development, Production)
               .Select(KeyFilter.Any, builder.Environment.EnvironmentName)
               // Configure Key Vault integration
               .ConfigureKeyVault(kv => kv.SetCredential(credential))
               // Configure refresh with sentinel key for live configuration updates
               .ConfigureRefresh(refreshOptions => {
                   // When the sentinel key changes, refresh all cached configuration values
                   refreshOptions.Register("Settings:Sentinel", refreshAll: true)
                   .SetRefreshInterval(TimeSpan.FromSeconds(30));
               });
    });
}
var isAppConfigEnabled = !string.IsNullOrEmpty(builder.Configuration["AppConfig:Endpoint"]);
if (isAppConfigEnabled) {
    builder.Services.AddAzureAppConfiguration();
}

// ForwardedHeaders — trust all proxies so Request.IsHttps = true inside the container.
// ACA terminates TLS at the edge; the container receives plain HTTP on port 8080.
// Without this, Request.IsHttps is false, which causes browsers to silently drop the
// OIDC correlation cookie (Set-Cookie with Secure flag on a perceived-HTTP response).
// Using Configure<ForwardedHeadersOptions> + .Clear() is the Microsoft-recommended approach:
// it mutates the existing default list instances rather than replacing them, which reliably
// removes the loopback-only restriction across all .NET versions.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();  // Remove loopback-only default; trust all proxy IPs
    options.KnownProxies.Clear();   // ACA edge IP is dynamic, cannot be hardcoded
});

// Add services to the container.
builder.Services.AddControllers();

// Disable the built-in HostFilteringMiddleware (auto-registered by HostFilteringStartupFilter
// from WebHost.ConfigureWebDefaults). It rejects requests whose Host header is not in the
// AllowedHosts list — including ACA liveness probe requests that use an internal pod IP
// (e.g. Host: 100.100.1.33:8080) — before any user middleware can run.
// Our custom HostHeaderValidationMiddleware handles host validation with the required
// /health path exemption for ACA liveness probes.
builder.Services.PostConfigure<HostFilteringOptions>(options =>
{
    options.AllowedHosts = ["*"];
});

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

// Application Insights - Enable telemetry if connection string is configured
var appInsightsConnectionString = builder.Configuration.GetConnectionString("ApplicationInsights")
    ?? builder.Configuration["ApplicationInsights:ConnectionString"];

if (!string.IsNullOrEmpty(appInsightsConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry(options =>
    {
        options.ConnectionString = appInsightsConnectionString;
        options.EnableQuickPulseMetricStream = true;
    });
}

// Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<DataProtectionHealthCheck>("data_protection_blob");

// Data Protection Monitoring
builder.Services.AddDataProtectionMonitoring();

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
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Name = "MotorcycleRAG"; // No __Host- prefix: ACA terminates TLS at edge; container receives plain HTTP
    options.Cookie.Path = "/";
    options.Cookie.IsEssential = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(1);
    options.SlidingExpiration = true;
    options.Cookie.MaxAge = TimeSpan.FromHours(1);
    options.Cookie.Domain = null; // Prevent subdomain attacks
})
.AddOpenIdConnect(options => {
    var authConfig = builder.Configuration.GetSection("AzureAd");
    // CIAM (Entra External ID) discovery document at /v2.0/.well-known/openid-configuration
    // returns issuer "https://{tenantId}.ciamlogin.com/{tenantId}/v2.0".
    // Without /v2.0, the v1 discovery doc returns an issuer without the /v2.0 suffix,
    // which does NOT match the actual iss claim in CIAM-issued tokens → IDX10205 issuer
    // validation failure → OnRemoteFailure → redirect loop.
    options.Authority = $"{authConfig["Instance"]}{authConfig["TenantId"]}/v2.0";
    options.ClientId = authConfig["ClientId"];
    options.ClientSecret = builder.Configuration["AzureAd:ClientSecret"]
        ?? throw new InvalidOperationException(
            "BFF Client Secret is not configured. " +
            "For local development, use: dotnet user-secrets set \"AzureAd:ClientSecret\" \"your-secret\" " +
            "--project 1-Presentation/MotorcycleRag.WebUI.BFF");
    options.ResponseType = "code";
    options.SaveTokens = true;
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("offline_access"); // Request refresh token
    // API scopes: the BFF is pre-authorized in the API app registration so no user consent prompt is shown.
    // CIAM will issue a v2 access token with audience=api://motorcyclerag-api and the correct CIAM issuer.
    options.Scope.Add("api://motorcyclerag-api/read");
    options.Scope.Add("api://motorcyclerag-api/chat");

    // Redirect hardening
    // RequireNonce: validated by the OIDC handler via the nonce cookie (Data Protected).
    // RequireState / RequireStateValidation are intentionally omitted: the handler never
    // populates OpenIdConnectProtocolValidationContext.State, so setting RequireStateValidation=true
    // always throws IDX21329. State security is enforced by the correlation cookie + PKCE + Data Protection.
    options.ProtocolValidator.RequireNonce = true;

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

    // ACA terminates TLS at the edge; the container receives plain http:// requests.
    // The OIDC middleware constructs redirect_uri before ForwardedHeaders runs, so we
    // must force https:// here to satisfy Entra ID's registered reply URL.
    options.Events = new OpenIdConnectEvents
    {
        OnRedirectToIdentityProvider = context =>
        {
            context.ProtocolMessage.RedirectUri = context.ProtocolMessage.RedirectUri
                .Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);
            return Task.CompletedTask;
        },
        // Handle OIDC remote failures (e.g. correlation state cookie lost mid-deployment,
        // stale nonce, cancelled login) gracefully instead of letting the exception
        // propagate unhandled and crash the container.
        OnRemoteFailure = context =>
        {
            context.HandleResponse();
            // Redirect to sign-in with a hint — the user can retry cleanly.
            context.Response.Redirect("/signin?error=auth_failed");
            return Task.CompletedTask;
        }
    };
    // OIDC correlation + nonce cookies must survive the cross-site Entra redirect callback.
    // Browser drops SameSite=Strict/Lax cookies on cross-site POST/redirect; SameSite=None allows them.
    options.NonceCookie.SameSite = SameSiteMode.None;
    options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
    options.CorrelationCookie.SameSite = SameSiteMode.None;
    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
});

// DataProtection: persist keys to Azure Blob Storage for container resilience
// Reads from App Config (DataProtection:BlobUri)
var dpBlobUri = builder.Configuration["DataProtection:BlobUri"];

if (!string.IsNullOrEmpty(dpBlobUri))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("MotorcycleRag.WebUI.BFF")
        .PersistKeysToAzureBlobStorage(new Uri(dpBlobUri), new Azure.Identity.DefaultAzureCredential());
}
else
{
    // Ephemeral keys — sessions will not survive container restarts
    // Set DataProtection:BlobUri in App Config to fix
    builder.Services.AddDataProtection()
        .SetApplicationName("MotorcycleRag.WebUI.BFF");
    
    // Fail fast in production if blob URI is not configured
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "DataProtection:BlobUri is required in production. " +
            "Configure 'DataProtection:BlobUri' in App Configuration to persist encryption keys. " +
            "Example: https://<storage-account>.blob.core.windows.net/<container>/keys.xml");
    }
}

var app = builder.Build();

// Get the application logger and telemetry client for Data Protection monitoring
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var monitoringService = app.Services.GetService<DataProtectionMonitoringService>();

if (!string.IsNullOrEmpty(dpBlobUri))
{
    logger.LogInformation("Data Protection keys persisted to Azure Blob Storage: {BlobUri}", dpBlobUri);
    
    // Track key initialization event
    monitoringService?.TrackKeysInitialized(dpBlobUri);
}
else if (!builder.Environment.IsProduction())
{
    logger.LogWarning(
        "Data Protection is using ephemeral keys - sessions will NOT survive container restarts. " +
        "Set 'DataProtection:BlobUri' in App Config to enable persistent key storage.");
}

if (isAppConfigEnabled) {
    app.UseAzureAppConfiguration();
}

// Pipeline - Security-first approach

// 0. Forwarded Headers — MUST be first so all subsequent middleware sees the correct
//    scheme/host as set by the Azure Container Apps edge proxy (X-Forwarded-Proto etc.).
//    Options are registered as a service (builder.Services.Configure above) and .Clear()
//    is called on the existing list instances — this is the Microsoft-recommended pattern.
//    Using inline ForwardedHeadersOptions with collection-expression initialiser syntax
//    (e.g. KnownIPNetworks = { }) creates a fresh empty list but does NOT call .Clear() on
//    the default instance, which means the loopback-only restriction can survive in some
//    .NET versions and cause Request.IsHttps to remain false inside the container.
app.UseForwardedHeaders();

// 1. HTTPS enforcement is intentionally omitted: ACA terminates TLS at the edge
//    and the container only receives plain HTTP on port 8080. Calling UseHttpsRedirection()
//    here causes a 500 when no HTTPS port is configured (e.g. health probes, any request
//    where X-Forwarded-Proto is absent). ACA's ingress enforces HTTPS externally.

// 2. HSTS (HTTP Strict Transport Security) - instructs browsers to always use HTTPS
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

// Health Check endpoint
app.MapHealthChecks("/health");

// Fallback to React (SPA)
app.MapFallbackToFile("index.html");

#pragma warning restore CA1506 // Avoid excessive class coupling

try
{
    await app.RunAsync().ConfigureAwait(false);
}
catch (Exception ex)
{
    // Surface any startup exception to stderr so it appears in container logs
    await Console.Error.WriteLineAsync($"FATAL STARTUP ERROR: {ex}").ConfigureAwait(false);

    // Track the fatal exception via the bootstrap TelemetryClient so it reaches
    // App Insights even if the DI-managed telemetry pipeline never started.
    if (bootstrapTelemetry is not null)
    {
        bootstrapTelemetry.TrackException(ex, new Dictionary<string, string>
        {
            ["source"] = "bootstrap",
            ["severity"] = "fatal"
        });
        bootstrapTelemetry.Flush();
        // Give the background sender a moment to dispatch the telemetry before the process exits.
        await Task.Delay(2000).ConfigureAwait(false);
    }

    throw;
}
