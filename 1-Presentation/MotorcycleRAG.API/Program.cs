using Microsoft.AspNetCore.Authorization;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.API.Configuration.Services;
using MotorcycleRAG.API.Extensions;
using MotorcycleRAG.API.Middleware;
using MotorcycleRAG.API.Services;
using MotorcycleRAG.Application.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Azure.Identity;
using Azure.Core;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Contracts.Interfaces;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace MotorcycleRAG.API;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Program class must be public for functional tests.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1052:Static holder types should be Static or NotInheritable",
    Justification = "Program class must be valid generic type argument for " +
    "WebApplicationFactory.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "Composition root inherently has high coupling")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "CodeQuality",
    "S1118:Add a static constructor to initialize static fields",
    Justification = "Program class does not use static fields requiring initialization")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "S1200:Split this class into smaller and more specialized ones",
    Justification = "Composition root naturally has many dependencies")]
public class Program {
    public static async Task Main(string[] args) {
        var builder = WebApplication.CreateBuilder(args);

        // Configure logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        // Configure structured logging
        if (builder.Environment.IsProduction()) {
            builder.Logging.AddJsonConsole();
        }

        // Add Azure App Configuration & Key Vault
        var appConfigEndpoint = builder.Configuration["AppConfig:Endpoint"];
        if (!string.IsNullOrEmpty(appConfigEndpoint)) {
            // Use ManagedIdentityCredential in non-development environments so cold-start auth
            // goes directly to the IMDS endpoint instead of cycling through DefaultAzureCredential's
            // full provider chain (WorkloadIdentity → EnvironmentCredential → VisualStudio → …),
            // which adds several seconds and can exceed the App Configuration startup timeout.
            TokenCredential credential = builder.Environment.IsDevelopment()
                ? new DefaultAzureCredential()
                : new ManagedIdentityCredential();
            builder.Configuration.AddAzureAppConfiguration(options => {
                options.Connect(new Uri(appConfigEndpoint), credential)
                       // Load all non-labelled keys
                       .Select(KeyFilter.Any)
                       // Load API-specific labelled keys (overrides unlabelled keys for API)
                       .Select(KeyFilter.Any, "api")
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

        // Refresh builder configuration to include AppConfig values
        var configuration = builder.Configuration;
        // Flag indicating whether Azure App Configuration is enabled
        var appConfigEndpointConfigured = configuration["AppConfig:Endpoint"];
        var isAppConfigEnabled = !string.IsNullOrEmpty(appConfigEndpointConfigured);

        if (isAppConfigEnabled) {
            // Registers IAzureAppConfigurationRefresher and other required services
            builder.Services.AddAzureAppConfiguration();
        }

        // Validate Application Insights configuration early
        var appInsightsSection = configuration.GetSection("ApplicationInsights");
        var enableTelemetry = appInsightsSection.GetValue<bool>("EnableTelemetry", false);
        var appInsightsConnectionString = configuration.GetConnectionString("ApplicationInsights");

        // Fail fast if telemetry is enabled but connection string is not configured
        if (enableTelemetry && string.IsNullOrWhiteSpace(appInsightsConnectionString)) {
            throw new InvalidOperationException(
                "Application Insights is enabled (EnableTelemetry=true) but ConnectionString is not configured. " +
                "For local development, use: dotnet user-secrets set \"ConnectionStrings:ApplicationInsights\" \"your-connection-string\" " +
                "--project 1-Presentation/MotorcycleRAG.API");
        }

        // Add Application Insights telemetry only if connection string is provided
        if (!string.IsNullOrEmpty(appInsightsConnectionString)) {
            builder.Services.AddApplicationInsightsTelemetry(options => {
                options.ConnectionString = appInsightsConnectionString;
                options.EnableAdaptiveSampling = true;
                options.EnableQuickPulseMetricStream = true;
                options.EnablePerformanceCounterCollectionModule = configuration.GetValue<bool>("ApplicationInsights:EnablePerformanceCounters", true);
            });

            // Add custom telemetry initializer
            builder.Services.AddSingleton<Microsoft.ApplicationInsights.Extensibility.ITelemetryInitializer, CustomTelemetryInitializer>();
        }

        // Add services to the container
        // Rate limiting is applied globally via MapControllers().RequireRateLimiting("authenticated")
        builder.Services.AddControllers();

        // Enforce maximum request body size (50MB) for security and DoS mitigation
        // This matches the application-level validation in FileUploadService
        builder.WebHost.ConfigureKestrel(options => {
            options.Limits.MaxRequestBodySize = 50 * 1024 * 1024;
        });

        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options => {
            options.MultipartBodyLengthLimit = 50 * 1024 * 1024;
        });

        // Register context-aware services
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Configure JSON serialization
        builder.Services.ConfigureJsonSerialization(builder.Environment.IsDevelopment());

        // Bind the entire configuration hierarchy into a single strongly-typed object that can be injected
        builder.Services.Configure<AppOptions>(configuration);

        // Consumers are encouraged to depend on IOptionsMonitor<AppOptions> so they receive live updates when the
        // sentinel key changes in Azure App Configuration.

        // Configure API documentation
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c => {
            c.SwaggerDoc("v1", new() {
                Title = "Motorcycle RAG API",
                Version = "v1",
                Description = "AI-powered motorcycle information retrieval system"
            });

            // Enable file upload support for [FromForm] IFormFile parameters
            c.MapType<IFormFile>(() => new Microsoft.OpenApi.OpenApiSchema
            {
                Type = Microsoft.OpenApi.JsonSchemaType.String,
                Format = "binary"
            });
        });

        // Configure CORS with strict security controls
        // OWASP A01:2021 - CSRF Risk Mitigation
        // Only allows explicitly configured origins and limits HTTP methods to necessary operations
        var corsOrigins = configuration["Cors:AllowedOrigins"]?.Split(";", StringSplitOptions.RemoveEmptyEntries)
            ?? new[] { "https://localhost:3000" }; // Default for local development only

        builder.Services.AddCors(options => {
            options.AddDefaultPolicy(policy => {
                policy.WithOrigins(corsOrigins)
                      .AllowCredentials() // Support HttpOnly cookies for secure auth
                      .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS") // Exclude PATCH, CONNECT, TRACE
                      .WithHeaders("Content-Type", "Authorization", "X-Requested-With") // Whitelist specific headers
                      .WithExposedHeaders("X-Total-Count") // Only expose necessary headers
                      .SetPreflightMaxAge(TimeSpan.FromSeconds(600)); // 10-minute preflight cache
            });
        });

        // Validate and populate Azure AD configuration from environment variables
        ValidateAndPopulateAzureAdConfiguration(configuration);

        // Validate and populate Azure AI endpoints from environment variables
        ValidateAndPopulateAzureAIConfiguration(configuration);

        // Configure custom services with validation
        try {
            builder.Services.AddAzureAIServices(configuration);
            builder.Services.AddCoreServices();
            builder.Services.AddSearchAgents(configuration);
            builder.Services.AddDataProcessors(configuration);
            builder.Services.AddDataPipelineServices(configuration);
            builder.Services.AddCachingAndOptimization(configuration);
            builder.Services.AddSqlPersistence(configuration);
            builder.Services.AddWebTrustPolicyServices(configuration);
            var healthChecksBuilder = builder.Services.AddHealthChecks();
            healthChecksBuilder.AddHealthChecks(configuration);

            // Add dual-issuer JWT bearer authentication
            // Supports tokens from BOTH Entra ID (workforce/admin users) and Entra External ID/B2C (customer users)
            // Hard invariant: The API MUST NOT accept cross-issuer tokens (token.iss must match one of the configured issuers)
            var authenticationBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
            
            // Note: We use a separate logger factory for startup logging to avoid BuildServiceProvider anti-pattern
            using var startupLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
            var startupLogger = startupLoggerFactory.CreateLogger("Program");
            authenticationBuilder.AddDualIssuerJwtBearer(builder.Configuration, startupLogger);

            // Add authorization policies for admin roles
            // Per spec.md (FR-038e.8) and plan.md: Admin-only operations require BOTH:
            //   1. "admin" in the 'scp' (scope) claim
            //   2. An allowed value in the 'roles' claim (e.g., mcr-api-admin, ContentAdmin, SuperAdmin)
            builder.Services.AddAuthorization(options => {
                // Local function to check for scopes
                static bool HasScope(System.Security.Claims.ClaimsPrincipal user, string requiredScope) {
                    if (user == null) {
                        return false;
                    }

                    // Log scope check for debugging
                    var scpClaims = user.FindAll("scp").Select(c => c.Value);
                    var schemaClaims = user.FindAll("http://schemas.microsoft.com/identity/claims/scope").Select(c => c.Value);
                    
                    var scopeClaims = scpClaims.Concat(schemaClaims);

                    foreach (var scopeClaim in scopeClaims) {
                        var scopes = scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (scopes.Any(s => string.Equals(s, requiredScope, StringComparison.OrdinalIgnoreCase))) {
                            return true;
                        }
                    }

                    return false;
                }

                // Local function to check for roles
                static bool HasAnyRole(System.Security.Claims.ClaimsPrincipal user, params string[] roles) {
                    if (user == null) {
                        return false;
                    }

                    // Check standard Role claim type AND "roles" claim type (common in Entra ID access tokens)
                    var roleClaims = user.FindAll(System.Security.Claims.ClaimTypes.Role)
                        .Select(c => c.Value)
                        .Concat(user.FindAll("roles").Select(c => c.Value));

                    return roleClaims.Any(role => roles.Any(allowed =>
                        string.Equals(role, allowed, StringComparison.OrdinalIgnoreCase)));
                }

                // Helper to validate client isolation (azp claim)
                static bool IsAuthorizedClient(System.Security.Claims.ClaimsPrincipal user, string expectedClientId) {
                    if (user == null || string.IsNullOrEmpty(expectedClientId)) {
                        return false;
                    }
                    // azp (Authorized Party) claim contains the client ID of the app that requested the token
                    var azp = user.FindFirst("azp")?.Value;
                    return string.Equals(azp, expectedClientId, StringComparison.OrdinalIgnoreCase);
                }

                // Get Admin Client ID from configuration for isolation checks
                var adminClientId = builder.Configuration["AzureAd:AdminClientId"];

                // Provide a dummy Client ID for testing environment if not set
                if (string.IsNullOrEmpty(adminClientId) && builder.Environment.IsEnvironment("Testing")) {
                    adminClientId = "11111111-1111-1111-1111-111111111111";
                }

                // Admin policy - requires BOTH admin scope AND admin app role AND correct Client ID
                options.AddPolicy("mcr-api-admin", policy => {
                    policy.RequireAuthenticatedUser();
                    policy.RequireAssertion(ctx => {
                        var hasScope = HasScope(ctx.User, "admin");
                        var hasRole = HasAnyRole(ctx.User, "mcr-api-admin");
                        var isAuthorizedClient = IsAuthorizedClient(ctx.User, adminClientId!);

                        // In testing, we enforce roles strictly but can be flexible with scope/client if headers are used instead of JWT
                        if (builder.Environment.IsEnvironment("Testing")) {
                            return hasRole && (hasScope || ctx.User.HasClaim("X-Test-Auth", "mcr-api-admin"));
                        }

                        // ... logging ...

                        return hasScope && hasRole && isAuthorizedClient;
                    });
                });

                // Read policy - requires read scope
                options.AddPolicy("Read", policy => {
                    policy.RequireAuthenticatedUser();
                    policy.RequireAssertion(ctx => HasScope(ctx.User, "read"));
                });

                // Chat policy - requires chat scope
                options.AddPolicy("Chat", policy => {
                    policy.RequireAuthenticatedUser();
                    policy.RequireAssertion(ctx => HasScope(ctx.User, "chat"));
                });

                // User policy - requires User app role (no scope requirement for regular users)
                options.AddPolicy("User", policy => {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "User");
                });

                // Viewer policy - requires Viewer app role (no scope requirement for read-only access)
                options.AddPolicy("Viewer", policy => {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "Viewer");
                });

                // Manuals view policy - allows User, Viewer, and admin roles to view manual pages
                // See AuthorizationPolicyNames.ManualsView for the constant.
                options.AddPolicy(MotorcycleRAG.API.Configuration.AuthorizationPolicyNames.ManualsView, policy => {
                    policy.RequireAuthenticatedUser();
                    policy.RequireAssertion(ctx =>
                        HasAnyRole(ctx.User, "User", "Viewer", "mcr-api-admin"));
                });

                // Default policy - requires any authenticated user and defaults to denying anonymous access
                // This "FallbackPolicy" ensures that every endpoint requires authentication unless marked [AllowAnonymous]
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();

                options.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });

            // Add rate limiting for public endpoints
            builder.Services.AddRateLimiter(options => {
                options.AddFixedWindowLimiter("public", rateLimiterOptions => {
                    rateLimiterOptions.Window = TimeSpan.FromSeconds(10);
                    rateLimiterOptions.PermitLimit = 100;
                    rateLimiterOptions.QueueLimit = 50;
                });

                // Role-based rate limiting per user (using 'oid' claim as partition key)
                options.AddPolicy("authenticated", context => {
                    var user = context.User;
                    
                    // Default limits for unknown/unauthenticated users (though they shouldn't hit this policy)
                    var limit = 50;
                    var window = TimeSpan.FromHours(1);
                    
                    // We need to redefine role checking logic here because the local function 'HasAnyRole'
                    // from AddAuthorization is not accessible in this scope.
                    static bool HasAnyRoleLocal(System.Security.Claims.ClaimsPrincipal p, params string[] r) {
                        if (p == null) return false;
                        var roleClaims = p.FindAll(System.Security.Claims.ClaimTypes.Role)
                            .Select(c => c.Value)
                            .Concat(p.FindAll("roles").Select(c => c.Value));
                        return roleClaims.Any(role => r.Any(allowed =>
                            string.Equals(role, allowed, StringComparison.OrdinalIgnoreCase)));
                    }

                    if (user.Identity?.IsAuthenticated == true) {
                        // Check for roles and assign limits based on spec
                        // Roadrunner / mcr-api-admin: Unlimited
                        if (HasAnyRoleLocal(user, "Roadrunner", "mcr-api-admin")) {
                            limit = 100000; // Effectively unlimited for practical purposes
                        }
                        // ProUser: 500/hour
                        else if (HasAnyRoleLocal(user, "ProUser")) {
                            limit = 500;
                        }
                        // DemoUser / Default: 50/hour
                        else {
                            limit = 50;
                        }
                    }

                    // Partition by user object ID (oid) to track individual usage
                    // Fallback to "anonymous" if no oid found (shouldn't happen for authenticated)
                    var partitionKey = user.FindFirst("oid")?.Value ?? "anonymous";

                    return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions {
                        PermitLimit = limit,
                        Window = window,
                        QueueLimit = 10,
                        AutoReplenishment = true
                    });
                });


                // Admin-only ingestion endpoints: Admin-tier limits (effectively unlimited)
                options.AddPolicy("ingestion-jobs", context => {
                    var partitionKey = context.User.FindFirst("oid")?.Value ?? "anonymous";
                    return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions {
                        PermitLimit = 100_000,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 10,
                        AutoReplenishment = true
                    });
                });

                // Manual page viewing: Pro-tier limits (500/hour)
                options.AddPolicy("manuals-view", context => {
                    var partitionKey = context.User.FindFirst("oid")?.Value ?? "anonymous";
                    return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions {
                        PermitLimit = 500,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 10,
                        AutoReplenishment = true
                    });
                });

                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            });
        }
        catch (Exception ex) {
            // Log configuration errors during startup before rethrowing
            // This ensures the error is recorded in application logs while preventing startup
            using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
            var startupLogger = loggerFactory.CreateLogger("Program");
            startupLogger.LogCritical(ex, "Failed to configure services during startup");
            throw new InvalidOperationException("Service configuration failed during startup. See logs for details.", ex);
        }

        var app = builder.Build();

        // Configure the HTTP request pipeline
        if (app.Environment.IsDevelopment()) {
            app.UseSwagger();
            app.UseSwaggerUI(c => {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Motorcycle RAG API v1");
                c.RoutePrefix = string.Empty; // Serve Swagger UI at root
            });
        }

        // Enable automatic refresh of configuration values from Azure App Configuration
        var isAppConfigEndpointConfigured = !string.IsNullOrEmpty(appConfigEndpointConfigured);
        if (isAppConfigEndpointConfigured) {
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
        // Controllers with [RateLimited] attributes will automatically have the rate limiter applied
        app.MapControllers().RequireRateLimiting("authenticated");

        // Map global health check endpoint to use "public" policy (not authenticated, higher limit)
        // Health checks should be accessible to monitoring systems without authentication
        // Returns structured JSON response with individual dependency status
        app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions {
            ResponseWriter = HealthCheckResponseWriter.WriteResponse,
            AllowCachingResponses = false
        }).AllowAnonymous().RequireRateLimiting("public");

        // Log startup information
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
        logger.LogInformation("Motorcycle RAG API starting up...");
        logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

        // Pre-warm the JWT signing key cache to avoid blocking on first request
        // This is critical to prevent deadlocks under concurrent load
        try {
            var authConfig = builder.Configuration.GetSection("Authentication:Issuers");
            var workforceIssuer = authConfig["Workforce"];
            var externalIdIssuer = authConfig["ExternalId"];

            if (!string.IsNullOrEmpty(workforceIssuer)) {
                var signingKeyCache = app.Services.GetRequiredService<SigningKeyCache>();
                logger.LogInformation("Pre-warming JWT signing key cache...");
                await signingKeyCache.PreWarmCacheAsync(workforceIssuer, externalIdIssuer);
                logger.LogInformation("JWT signing key cache pre-warming completed");
            }
            else {
                logger.LogWarning("Workforce issuer not configured - signing key cache will not be pre-warmed");
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, "Error pre-warming JWT signing key cache. Application will continue but JWT validation may fail on first request.");
        }

        await app.RunAsync();
    }

    /// <summary>
    /// Validates and populates Azure AD configuration from IConfiguration.
    /// Reads from: 1) User Secrets, 2) App Config, 3) appsettings.{Environment}.json (via standard .NET config system).
    /// Derives JWT issuer URLs and audience values from TenantId and ClientId.
    /// </summary>
    /// <param name="configuration">The application configuration (includes merged appsettings, user secrets, env vars, App Config)</param>
    private static void ValidateAndPopulateAzureAdConfiguration(IConfiguration configuration) {
        // Read from IConfiguration, which has already merged all sources in priority order:
        // User Secrets > Environment Variables > App Config > appsettings.Development.json
        var tenantId = configuration["AzureAd:TenantId"];
        var clientId = configuration["AzureAd:ClientId"];

        // Fail fast if required values are missing
        if (string.IsNullOrWhiteSpace(tenantId)) {
            throw new InvalidOperationException(
                "Azure AD Tenant ID is not configured. " +
                "Set AzureAd:TenantId in appsettings.Development.json or use: " +
                "dotnet user-secrets set \"AzureAd:TenantId\" \"your-tenant-id\" --project 1-Presentation/MotorcycleRAG.API");
        }

        if (string.IsNullOrWhiteSpace(clientId)) {
            throw new InvalidOperationException(
                "Azure AD Client ID is not configured. " +
                "Set AzureAd:ClientId in appsettings.Development.json or use: " +
                "dotnet user-secrets set \"AzureAd:ClientId\" \"your-client-id\" --project 1-Presentation/MotorcycleRAG.API");
        }

        // Log configuration source for audit trail
        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var startupLogger = loggerFactory.CreateLogger("Program");
        startupLogger.LogInformation("Azure AD configuration loaded from AzureAd:TenantId and AzureAd:ClientId");

        // Derive JWT issuer URLs and audience from TenantId and ClientId
        var azureAdSection = new ConfigurationBuilder()
            .AddInMemoryCollection((IEnumerable<KeyValuePair<string, string?>>)new Dictionary<string, string?>
            {
                { "AzureAd:Audience", clientId }, // Audience typically matches ClientId
                { "Authentication:Audience", clientId },
                { "Jwt:ValidAudience", clientId },
                { "Jwt:ValidIssuer", $"https://login.microsoftonline.com/{tenantId}/v2.0" },
                { "Jwt:IssuerSigningKeyUrl", $"https://login.microsoftonline.com/{tenantId}/discovery/v2.0/keys" },
                { "Authentication:Issuers:Workforce", $"https://login.microsoftonline.com/{tenantId}/v2.0" }
            })
            .Build();

        // Merge derived config into the existing configuration
        foreach (var kvp in azureAdSection.AsEnumerable().Where(x => x.Value != null)) {
            ((IConfigurationBuilder)configuration).AddInMemoryCollection(new[] { kvp });
        }
    }

    /// <summary>
    /// Validates Azure AI service endpoints from IConfiguration.
    /// Reads from: 1) User Secrets, 2) App Config, 3) appsettings.{Environment}.json (via standard .NET config system).
    /// Ensures endpoints are HTTPS URLs and not empty/placeholder values.
    /// OPTIONAL: Services can be configured later. App will run in degraded state if not configured.
    /// </summary>
    /// <param name="configuration">The application configuration (includes merged appsettings, user secrets, env vars, App Config)</param>
    private static void ValidateAndPopulateAzureAIConfiguration(IConfiguration configuration) {
        // Read from IConfiguration, which has already merged all sources in priority order
        var openAIEndpoint = configuration["AzureAI:OpenAIEndpoint"];
        var searchEndpoint = configuration["AzureAI:SearchServiceEndpoint"];
        var documentIntelligenceEndpoint = configuration["AzureAI:DocumentIntelligenceEndpoint"];
        var foundryEndpoint = configuration["AzureAI:FoundryEndpoint"];

        // Validate endpoints ONLY if they are provided (optional for degraded mode)
        ValidateEndpointIfProvided("OpenAI", openAIEndpoint, "AzureAI:OpenAIEndpoint");
        ValidateEndpointIfProvided("Search", searchEndpoint, "AzureAI:SearchServiceEndpoint");
        ValidateEndpointIfProvided("Document Intelligence", documentIntelligenceEndpoint, "AzureAI:DocumentIntelligenceEndpoint");
        ValidateEndpointIfProvided("Foundry", foundryEndpoint, "AzureAI:FoundryEndpoint");

        // Log configuration status for audit trail
        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var startupLogger = loggerFactory.CreateLogger("Program");

        var configuredServices = new List<string>();
        if (!string.IsNullOrWhiteSpace(openAIEndpoint)) configuredServices.Add("OpenAI");
        if (!string.IsNullOrWhiteSpace(searchEndpoint)) configuredServices.Add("Search");
        if (!string.IsNullOrWhiteSpace(documentIntelligenceEndpoint)) configuredServices.Add("Document Intelligence");
        if (!string.IsNullOrWhiteSpace(foundryEndpoint)) configuredServices.Add("Foundry");

        if (configuredServices.Any()) {
            startupLogger.LogInformation(
                "Azure AI services configured: {Services}. Other services will run in degraded mode.",
                string.Join(", ", configuredServices));
        } else {
            startupLogger.LogWarning(
                "No Azure AI services configured. App running in degraded mode. " +
                "Set AzureAI:OpenAIEndpoint, AzureAI:SearchServiceEndpoint, " +
                "AzureAI:DocumentIntelligenceEndpoint, AzureAI:FoundryEndpoint via user secrets or App Config.");
        }
    }

    /// <summary>
    /// Validates a single Azure service endpoint if provided.
    /// Ensures endpoint is a valid HTTPS URL (not a placeholder or example value).
    /// If not provided, the service will run in degraded mode.
    /// </summary>
    private static void ValidateEndpointIfProvided(string serviceName, string? endpoint, string configKey) {
        // If not provided, skip validation - service will run in degraded mode
        if (string.IsNullOrWhiteSpace(endpoint)) {
            return;
        }

        const string YourPrefix = "your-";
        const string ExampleKeyword = "example";
        const string PlaceholderKeyword = "placeholder";
        const string CurrentLabel = nameof(endpoint);
        const string EndpointLabel = nameof(endpoint);

        // Verify endpoint is HTTPS
        if (!endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"Azure {serviceName} {EndpointLabel} MUST use HTTPS protocol for security. " +
                $"Current {CurrentLabel}: {endpoint}. " +
                $"Update {configKey} in appsettings.Development.json or use: " +
                $"dotnet user-secrets set \"{configKey}\" \"your-https-url\" --project 1-Presentation/MotorcycleRAG.API");
        }

        // Verify endpoint is a valid URI
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https") {
            throw new InvalidOperationException(
                $"Azure {serviceName} {EndpointLabel} is not a valid HTTPS URL. " +
                $"Current {CurrentLabel}: {endpoint}. " +
                $"Ensure {configKey} contains a properly formatted HTTPS URL in appsettings.Development.json or " +
                $"use: dotnet user-secrets set \"{configKey}\" \"your-https-url\" --project 1-Presentation/MotorcycleRAG.API");
        }

        // Warn if endpoint looks like a placeholder or example value
        if (endpoint.Contains(YourPrefix, StringComparison.OrdinalIgnoreCase) ||
            endpoint.Contains(ExampleKeyword, StringComparison.OrdinalIgnoreCase) ||
            endpoint.Contains(PlaceholderKeyword, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"Azure {serviceName} {EndpointLabel} appears to be a placeholder or example value. " +
                $"Current {CurrentLabel}: {endpoint}. " +
                $"Set {configKey} to your actual Azure service {EndpointLabel} URL in appsettings.Development.json or " +
                $"use: dotnet user-secrets set \"{configKey}\" \"your-actual-url\" --project 1-Presentation/MotorcycleRAG.API");
        }
    }

}

