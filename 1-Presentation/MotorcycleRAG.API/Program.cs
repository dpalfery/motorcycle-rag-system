using Microsoft.AspNetCore.Authorization;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.API.Configuration.Services;
using MotorcycleRAG.API.Extensions;
using MotorcycleRAG.API.Middleware;
using MotorcycleRAG.API.Services;
using MotorcycleRAG.Application.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Contracts.Interfaces;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;

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
            var credential = new DefaultAzureCredential();
            builder.Configuration.AddAzureAppConfiguration(options => {
                options.Connect(new Uri(appConfigEndpoint), credential)
                       // Load all non-labelled keys
                       .Select(KeyFilter.Any)
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
                "REQUIRED: Set the MCR_API_APPINSIGHTS_CONNECTION_STRING environment variable. " +
                "No fallback to configuration files is permitted for security compliance. " +
                "For development, use: dotnet user-secrets set \"MCR_API_APPINSIGHTS_CONNECTION_STRING\" \"your-connection-string\"");
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
            using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
            var startupLogger = loggerFactory.CreateLogger("Program");
            authenticationBuilder.AddDualIssuerJwtBearer(builder.Configuration, startupLogger);

            // Add authorization policies for admin roles
            // Per spec.md (FR-038e.8) and plan.md: Admin-only operations require BOTH:
            //   1. "admin_access" in the 'scp' (scope) claim
            //   2. An allowed value in the 'roles' claim (e.g., Admin, DataAdmin, ContentAdmin, SuperAdmin)
            builder.Services.AddAuthorization(options => {
                static bool HasScope(System.Security.Claims.ClaimsPrincipal user, string requiredScope) {
                    if (user == null) {
                        return false;
                    }

                    var scopeClaims = user.FindAll("scp")
                        .Select(c => c.Value)
                        .Concat(user.FindAll("http://schemas.microsoft.com/identity/claims/scope").Select(c => c.Value));

                    foreach (var scopeClaim in scopeClaims) {
                        var scopes = scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (scopes.Any(s => string.Equals(s, requiredScope, StringComparison.OrdinalIgnoreCase))) {
                            return true;
                        }
                    }

                    return false;
                }

                static bool HasAnyRole(System.Security.Claims.ClaimsPrincipal user, params string[] roles) {
                    if (user == null) {
                        return false;
                    }

                    var roleClaims = user.FindAll(System.Security.Claims.ClaimTypes.Role)
                        .Select(c => c.Value)
                        .Concat(user.FindAll("roles").Select(c => c.Value));

                    return roleClaims.Any(role => roles.Any(allowed =>
                        string.Equals(role, allowed, StringComparison.OrdinalIgnoreCase)));
                }
                // Admin policy - requires BOTH admin_access scope AND Admin app role
                options.AddPolicy("Admin", policy => {
                    policy.RequireAuthenticatedUser();
                    policy.RequireAssertion(ctx =>
                        HasScope(ctx.User, "admin_access") &&
                        HasAnyRole(ctx.User, "Admin", "SuperAdmin"));
                });

                // DataAdmin policy - requires BOTH admin_access scope AND DataAdmin app role
                options.AddPolicy("DataAdmin", policy => {
                    policy.RequireAuthenticatedUser();
                    policy.RequireAssertion(ctx =>
                        HasScope(ctx.User, "admin_access") &&
                        HasAnyRole(ctx.User, "DataAdmin", "Admin", "SuperAdmin"));
                });

                // ContentAdmin policy - requires BOTH admin_access scope AND ContentAdmin app role
                options.AddPolicy("ContentAdmin", policy => {
                    policy.RequireAuthenticatedUser();
                    policy.RequireAssertion(ctx =>
                        HasScope(ctx.User, "admin_access") &&
                        HasAnyRole(ctx.User, "ContentAdmin", "Admin", "SuperAdmin"));
                });

                // SuperAdmin policy - requires BOTH admin_access scope AND SuperAdmin app role
                options.AddPolicy("SuperAdmin", policy => {
                    policy.RequireAuthenticatedUser();
                    policy.RequireAssertion(ctx =>
                        HasScope(ctx.User, "admin_access") &&
                        HasAnyRole(ctx.User, "SuperAdmin"));
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

                // Default policy - requires any authenticated user
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

                options.AddFixedWindowLimiter("authenticated", rateLimiterOptions => {
                    rateLimiterOptions.Window = TimeSpan.FromMinutes(1);
                    rateLimiterOptions.PermitLimit = 1000;
                    rateLimiterOptions.QueueLimit = 100;
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
        }).RequireRateLimiting("public");

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
    /// Validates and populates Azure AD configuration from secure sources.
    /// Reads from: 1) Environment variables (production), 2) User Secrets (development).
    /// No fallbacks to appsettings.json files are permitted for security compliance.
    /// </summary>
    /// <param name="configuration">The application configuration</param>
    private static void ValidateAndPopulateAzureAdConfiguration(IConfiguration configuration) {
        const string ConfigFilesLabel = nameof(configuration);

        // SECURITY: Read from environment variables OR user secrets (both are secure)
        // Priority: Environment variables > User Secrets > (fail - no appsettings.json fallback)
        var tenantId = Environment.GetEnvironmentVariable("MCR_API_AZURE_AD_TENANT_ID") 
                       ?? configuration["MCR_API_AZURE_AD_TENANT_ID"];
        var clientId = Environment.GetEnvironmentVariable("MCR_API_AZURE_AD_CLIENT_ID") 
                       ?? configuration["MCR_API_AZURE_AD_CLIENT_ID"];

        // Fail fast if required secrets are missing
        if (string.IsNullOrWhiteSpace(tenantId)) {
            throw new InvalidOperationException(
                "Azure AD Tenant ID is not configured. " +
                "REQUIRED: Set the MCR_API_AZURE_AD_TENANT_ID environment variable or user secret. " +
                $"No fallback to {ConfigFilesLabel} files is permitted for security compliance. " +
                $"For local development, use: dotnet user-secrets set \"MCR_API_AZURE_AD_TENANT_ID\" \"your-tenant-id\" --project 1-Presentation/MotorcycleRAG.API");
        }

        if (string.IsNullOrWhiteSpace(clientId)) {
            throw new InvalidOperationException(
                "Azure AD Client ID is not configured. " +
                "REQUIRED: Set the MCR_API_AZURE_AD_CLIENT_ID environment variable or user secret. " +
                $"No fallback to {ConfigFilesLabel} files is permitted for security compliance. " +
                $"For local development, use: dotnet user-secrets set \"MCR_API_AZURE_AD_CLIENT_ID\" \"your-client-id\" --project 1-Presentation/MotorcycleRAG.API");
        }

        // Log secret sources for audit trail
        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var startupLogger = loggerFactory.CreateLogger("Program");
        startupLogger.LogInformation("Azure AD configuration loaded from secure source (MCR_API_AZURE_AD_TENANT_ID, MCR_API_AZURE_AD_CLIENT_ID)");

        // Update configuration with environment values
        var azureAdSection = new ConfigurationBuilder()
            .AddInMemoryCollection((IEnumerable<KeyValuePair<string, string?>>)new Dictionary<string, string?>
            {
                { "AzureAd:TenantId", tenantId },
                { "AzureAd:ClientId", clientId },
                { "AzureAd:Audience", clientId }, // Audience typically matches ClientId
                { "Authentication:Audience", clientId },
                { "Jwt:ValidAudience", clientId },
                { "Jwt:ValidIssuer", $"https://login.microsoftonline.com/{tenantId}/v2.0" },
                { "Jwt:IssuerSigningKeyUrl", $"https://login.microsoftonline.com/{tenantId}/discovery/v2.0/keys" },
                { "Authentication:Issuers:Workforce", $"https://login.microsoftonline.com/{tenantId}/v2.0" }
            })
            .Build();

        // Merge environment-based config into the existing configuration
        foreach (var kvp in azureAdSection.AsEnumerable().Where(x => x.Value != null)) {
            ((IConfigurationBuilder)configuration).AddInMemoryCollection(new[] { kvp });
        }
    }

    /// <summary>
    /// Validates and populates Azure AI service endpoints from secure sources.
    /// Reads from: 1) Environment variables (production), 2) User Secrets (development).
    /// Ensures endpoints are HTTPS URLs and not empty/placeholder values.
    /// OPTIONAL: Services can be configured later. App will run in degraded state if not configured.
    /// </summary>
    /// <param name="configuration">The application configuration</param>
    private static void ValidateAndPopulateAzureAIConfiguration(IConfiguration configuration) {
        // SECURITY: Read from environment variables OR user secrets (both are secure)
        // Priority: Environment variables > User Secrets > null (degraded mode)
        var openAIEndpoint = Environment.GetEnvironmentVariable("MCR_API_AZURE_OPENAI_ENDPOINT")
                             ?? configuration["MCR_API_AZURE_OPENAI_ENDPOINT"];
        var searchEndpoint = Environment.GetEnvironmentVariable("MCR_API_AZURE_SEARCH_ENDPOINT")
                             ?? configuration["MCR_API_AZURE_SEARCH_ENDPOINT"];
        var documentIntelligenceEndpoint = Environment.GetEnvironmentVariable("MCR_API_AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT")
                                           ?? configuration["MCR_API_AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT"];
        var foundryEndpoint = Environment.GetEnvironmentVariable("MCR_API_AZURE_FOUNDRY_ENDPOINT")
                              ?? configuration["MCR_API_AZURE_FOUNDRY_ENDPOINT"];

        // Validate endpoints ONLY if they are provided (optional for degraded mode)
        ValidateEndpointIfProvided("OpenAI", openAIEndpoint, "MCR_API_AZURE_OPENAI_ENDPOINT");
        ValidateEndpointIfProvided("Search", searchEndpoint, "MCR_API_AZURE_SEARCH_ENDPOINT");
        ValidateEndpointIfProvided("Document Intelligence", documentIntelligenceEndpoint, "MCR_API_AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT");
        ValidateEndpointIfProvided("Foundry", foundryEndpoint, "MCR_API_AZURE_FOUNDRY_ENDPOINT");

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
                "Set MCR_API_AZURE_OPENAI_ENDPOINT, MCR_API_AZURE_SEARCH_ENDPOINT, " +
                "MCR_API_AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT, MCR_API_AZURE_FOUNDRY_ENDPOINT to enable features.");
        }

        // Update configuration with environment values (environment variables ONLY)
        var azureAIConfig = new Dictionary<string, string?>
        {
            { "AzureAI:OpenAIEndpoint", openAIEndpoint },
            { "AzureAI:SearchServiceEndpoint", searchEndpoint },
            { "AzureAI:DocumentIntelligenceEndpoint", documentIntelligenceEndpoint },
            { "AzureAI:FoundryEndpoint", foundryEndpoint }
        };

        var azureAISection = new ConfigurationBuilder()
            .AddInMemoryCollection((IEnumerable<KeyValuePair<string, string?>>)azureAIConfig)
            .Build();

        // Merge environment-based config into the existing configuration
        foreach (var kvp in azureAISection.AsEnumerable().Where(x => x.Value != null)) {
            ((IConfigurationBuilder)configuration).AddInMemoryCollection(new[] { kvp });
        }
    }

    /// <summary>
    /// Validates a single Azure service endpoint from environment variables if provided.
    /// Ensures endpoint is a valid HTTPS URL (not a placeholder or example value).
    /// If not provided, the service will run in degraded mode.
    /// </summary>
    private static void ValidateEndpointIfProvided(string serviceName, string? endpoint, string envVarName) {
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
                $"Update the {envVarName} environment variable with a valid HTTPS URL.");
        }

        // Verify endpoint is a valid URI
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https") {
            throw new InvalidOperationException(
                $"Azure {serviceName} {EndpointLabel} is not a valid HTTPS URL. " +
                $"Current {CurrentLabel}: {endpoint}. " +
                $"Ensure the {envVarName} environment variable contains a properly formatted HTTPS URL.");
        }

        // Warn if endpoint looks like a placeholder or example value
        if (endpoint.Contains(YourPrefix, StringComparison.OrdinalIgnoreCase) ||
            endpoint.Contains(ExampleKeyword, StringComparison.OrdinalIgnoreCase) ||
            endpoint.Contains(PlaceholderKeyword, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"Azure {serviceName} {EndpointLabel} appears to be a placeholder or example value. " +
                $"Current {CurrentLabel}: {endpoint}. " +
                $"Set the {envVarName} environment variable to your actual Azure service {EndpointLabel} URL.");
        }
    }

}

