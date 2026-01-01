using Microsoft.AspNetCore.Authorization;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.Application.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using MotorcycleRAG.Core.Options;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MotorcycleRAG.API.Middleware;
using Microsoft.AspNetCore.RateLimiting;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        // Configure structured logging
        if (builder.Environment.IsProduction())
        {
            builder.Logging.AddJsonConsole();
        }

        // Add Azure App Configuration & Key Vault
        var appConfigEndpoint = builder.Configuration["AppConfig:Endpoint"];
        if (!string.IsNullOrEmpty(appConfigEndpoint))
        {
            var credential = new DefaultAzureCredential();
            builder.Configuration.AddAzureAppConfiguration(options =>
            {
                options.Connect(new Uri(appConfigEndpoint), credential)
                       // Load all non-labelled keys
                       .Select(KeyFilter.Any, LabelFilter.Null)
                       // Load environment-specific labelled keys (e.g. Development, Production)
                       .Select(KeyFilter.Any, builder.Environment.EnvironmentName)
                       // Configure Key Vault integration
                       .ConfigureKeyVault(kv => kv.SetCredential(credential))
                       // Configure refresh with sentinel key for live configuration updates
                       .ConfigureRefresh(refreshOptions =>
                       {
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

        if (isAppConfigEnabled)
        {
            // Registers IAzureAppConfigurationRefresher and other required services
            builder.Services.AddAzureAppConfiguration();
        }

        // Validate Application Insights configuration early
        var appInsightsSection = configuration.GetSection("ApplicationInsights");
        var enableTelemetry = appInsightsSection.GetValue<bool>("EnableTelemetry", false);
        var appInsightsConnectionString = configuration.GetConnectionString("ApplicationInsights")
            ?? configuration["ApplicationInsights:ConnectionString"];

        // Fail fast if telemetry is enabled but connection string is not configured
        if (enableTelemetry && string.IsNullOrWhiteSpace(appInsightsConnectionString))
        {
            throw new InvalidOperationException(
                "Application Insights is enabled (EnableTelemetry=true) but ConnectionString is not configured. " +
                "Set the APPINSIGHTS_CONNECTION_STRING environment variable or set EnableTelemetry=false in appsettings. " +
                "For development, disable telemetry in appsettings.Development.json.");
        }

        // Add Application Insights telemetry only if connection string is provided
        if (!string.IsNullOrEmpty(appInsightsConnectionString))
        {
            builder.Services.AddApplicationInsightsTelemetry(options =>
            {
                options.ConnectionString = appInsightsConnectionString;
                options.EnableAdaptiveSampling = true;
                options.EnableQuickPulseMetricStream = true;
                options.EnablePerformanceCounterCollectionModule = configuration.GetValue<bool>("ApplicationInsights:EnablePerformanceCounters", true);
            });

            // Add custom telemetry initializer
            builder.Services.AddSingleton<Microsoft.ApplicationInsights.Extensibility.ITelemetryInitializer, CustomTelemetryInitializer>();
        }

        // Add services to the container with rate limiting conventions
        // The RateLimitingConvention scans all controllers for [RateLimited] attributes
        // and automatically applies the specified rate limiting policies to matching endpoints
        builder.Services.AddControllers();
        // TODO: RateLimitingConvention will be added in next commit

        // Configure JSON serialization
        builder.Services.ConfigureJsonSerialization(builder.Environment.IsDevelopment());

        // Bind the entire configuration hierarchy into a single strongly-typed object that can be injected
        builder.Services.Configure<AppOptions>(configuration);

        // Consumers are encouraged to depend on IOptionsMonitor<AppOptions> so they receive live updates when the
        // sentinel key changes in Azure App Configuration.

        // Configure API documentation
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new()
            {
                Title = "Motorcycle RAG API",
                Version = "v1",
                Description = "AI-powered motorcycle information retrieval system"
            });
        });

        // Configure CORS with strict security controls
        // OWASP A01:2021 - CSRF Risk Mitigation
        // Only allows explicitly configured origins and limits HTTP methods to necessary operations
        var corsOrigins = configuration["Cors:AllowedOrigins"]?.Split(";", StringSplitOptions.RemoveEmptyEntries)
            ?? new[] { "https://localhost:3000" }; // Default for local development only

        builder.Services.AddCors(options =>
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

        // Validate and populate Azure AD configuration from environment variables
        ValidateAndPopulateAzureAdConfiguration(configuration, builder.Environment);

        // Validate and populate Azure AI endpoints from environment variables
        ValidateAndPopulateAzureAIConfiguration(configuration, builder.Environment);

        // Configure custom services with validation
        try
        {
            builder.Services.AddAzureAIServices(configuration);
            builder.Services.AddCoreServices();
            builder.Services.AddSearchAgents();
            builder.Services.AddDataProcessors();
            builder.Services.AddDataPipelineServices(configuration);
            builder.Services.AddCachingAndOptimization(configuration);
            builder.Services.AddSqlPersistence(configuration);
            builder.Services.AddHealthChecks(configuration);

            // Add JWT bearer authentication
            // TODO: Dual-issuer JWT validation will be added in next commit
            // This will support tokens from BOTH Entra ID (workforce/admin users) and Entra External ID/B2C (customer users)
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = $"https://login.microsoftonline.com/{builder.Configuration["AzureAd:TenantId"]}/v2.0";
                    options.Audience = builder.Configuration["Jwt:ValidAudience"];
                    options.TokenValidationParameters.ValidateIssuer = true;
                    options.TokenValidationParameters.ValidIssuer = $"https://login.microsoftonline.com/{builder.Configuration["AzureAd:TenantId"]}/v2.0";
                });

            // Add authorization policies for admin roles
            // Per spec.md (FR-038e.8) and plan.md: Admin-only operations require BOTH:
            //   1. "admin_access" in the 'scp' (scope) claim
            //   2. An allowed value in the 'roles' claim (e.g., Admin, DataAdmin, ContentAdmin, SuperAdmin)
            builder.Services.AddAuthorization(options =>
            {
                // Admin policy - requires BOTH admin_access scope AND Admin app role
                options.AddPolicy("Admin", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim("scp", "admin_access"); // Scope requirement for admin operations
                    policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "Admin"); // Role requirement
                });

                // DataAdmin policy - requires BOTH admin_access scope AND DataAdmin app role
                options.AddPolicy("DataAdmin", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim("scp", "admin_access"); // Scope requirement for admin operations
                    policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "DataAdmin"); // Role requirement
                });

                // ContentAdmin policy - requires BOTH admin_access scope AND ContentAdmin app role
                options.AddPolicy("ContentAdmin", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim("scp", "admin_access"); // Scope requirement for admin operations
                    policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "ContentAdmin"); // Role requirement
                });

                // SuperAdmin policy - requires BOTH admin_access scope AND SuperAdmin app role
                options.AddPolicy("SuperAdmin", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim("scp", "admin_access"); // Scope requirement for admin operations
                    policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "SuperAdmin"); // Role requirement
                });

                // User policy - requires User app role (no scope requirement for regular users)
                options.AddPolicy("User", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "User");
                });

                // Viewer policy - requires Viewer app role (no scope requirement for read-only access)
                options.AddPolicy("Viewer", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "Viewer");
                });

                // Default policy - requires any authenticated user
                options.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });

            // Add rate limiting for public endpoints
            builder.Services.AddRateLimiter(options =>
            {
                options.AddFixedWindowLimiter("public", rateLimiterOptions =>
                {
                    rateLimiterOptions.Window = TimeSpan.FromSeconds(10);
                    rateLimiterOptions.PermitLimit = 100;
                    rateLimiterOptions.QueueLimit = 50;
                });

                options.AddFixedWindowLimiter("authenticated", rateLimiterOptions =>
                {
                    rateLimiterOptions.Window = TimeSpan.FromMinutes(1);
                    rateLimiterOptions.PermitLimit = 1000;
                    rateLimiterOptions.QueueLimit = 100;
                });

                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            });

            // Validate configuration early
            // ValidateConfiguration(configuration, builder.Environment);
        }
        catch (Exception ex)
        {
            // Log configuration errors during startup
            var startupLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<Program>();
            startupLogger.LogCritical(ex, "Failed to configure services during startup");
            throw;
        }

        var app = builder.Build();

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

        app.UseHttpsRedirection();

        // Enable automatic refresh of configuration values from Azure App Configuration
        var isAppConfigEndpointConfigured = !string.IsNullOrEmpty(appConfigEndpointConfigured);
        if (isAppConfigEndpointConfigured)
        {
            app.UseAzureAppConfiguration();
        }

        // Middleware order is critical for security:
        // 1. HTTPS redirection (enforce secure transport)
        // 2. Security headers (defense-in-depth)
        // 3. Correlation tracking (observability)
        // 4. Rate limiting (DOS/CSRF prevention - MUST be before CORS to prevent bypass)
        // 5. Exception handling (graceful error responses)
        // 6. CORS (restricted cross-origin access)
        // 7. Authentication (identity verification)
        // 8. Authorization (access control)

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
        app.MapHealthChecks("/health").RequireRateLimiting("public");

        // Log startup information
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Motorcycle RAG API starting up...");
        logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

        app.Run();
    }

    /// <summary>
    /// Validates and populates Azure AD configuration from environment variables.
    /// This ensures sensitive identifiers are not hardcoded in appsettings files.
    /// </summary>
    /// <param name="configuration">The application configuration</param>
    /// <param name="environment">The hosting environment</param>
    private static void ValidateAndPopulateAzureAdConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        var tenantId = Environment.GetEnvironmentVariable("AZURE_AD_TENANT_ID")
            ?? (configuration["AzureAd:TenantId"] != "" ? configuration["AzureAd:TenantId"] : null);

        var clientId = Environment.GetEnvironmentVariable("AZURE_AD_CLIENT_ID")
            ?? (configuration["AzureAd:ClientId"] != "" ? configuration["AzureAd:ClientId"] : null);

        // In production or when appsettings values are empty, environment variables are required
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException(
                "Azure AD Tenant ID is not configured. " +
                "Set the AZURE_AD_TENANT_ID environment variable or populate AzureAd:TenantId in appsettings.json. " +
                "For local development, use 'dotnet user-secrets set \"AzureAd:TenantId\" \"your-tenant-id\"'.");
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                "Azure AD Client ID is not configured. " +
                "Set the AZURE_AD_CLIENT_ID environment variable or populate AzureAd:ClientId in appsettings.json. " +
                "For local development, use 'dotnet user-secrets set \"AzureAd:ClientId\" \"your-client-id\"'.");
        }

        // Update configuration with environment values
        var azureAdSection = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
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
        foreach (var kvp in azureAdSection.AsEnumerable().Where(x => x.Value != null))
        {
            ((IConfigurationBuilder)configuration).AddInMemoryCollection(new[] { kvp });
        }
    }

    /// <summary>
    /// Validates and populates Azure AI service endpoints from environment variables.
    /// Ensures endpoints are HTTPS URLs and not empty/placeholder values.
    /// Environment variables take precedence over configuration file values.
    /// </summary>
    /// <param name="configuration">The application configuration</param>
    /// <param name="environment">The hosting environment</param>
    private static void ValidateAndPopulateAzureAIConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        // Load Azure AI endpoints from environment variables, falling back to config
        var openAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? (configuration["AzureAI:OpenAIEndpoint"] != "" ? configuration["AzureAI:OpenAIEndpoint"] : null);

        var searchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT")
            ?? (configuration["AzureAI:SearchServiceEndpoint"] != "" ? configuration["AzureAI:SearchServiceEndpoint"] : null);

        var documentIntelligenceEndpoint = Environment.GetEnvironmentVariable("AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT")
            ?? (configuration["AzureAI:DocumentIntelligenceEndpoint"] != "" ? configuration["AzureAI:DocumentIntelligenceEndpoint"] : null);

        var foundryEndpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_ENDPOINT")
            ?? (configuration["AzureAI:FoundryEndpoint"] != "" ? configuration["AzureAI:FoundryEndpoint"] : null);

        // Validate endpoints are provided and valid HTTPS URLs
        ValidateEndpoint("OpenAI", openAIEndpoint, "AZURE_OPENAI_ENDPOINT", environment.IsProduction());
        ValidateEndpoint("Search", searchEndpoint, "AZURE_SEARCH_ENDPOINT", environment.IsProduction());
        ValidateEndpoint("Document Intelligence", documentIntelligenceEndpoint, "AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT", environment.IsProduction());
        ValidateEndpoint("Foundry", foundryEndpoint, "AZURE_FOUNDRY_ENDPOINT", environment.IsProduction());

        // Update configuration with environment values (environment variables take precedence)
        var azureAIConfig = new Dictionary<string, string>
        {
            { "AzureAI:OpenAIEndpoint", openAIEndpoint },
            { "AzureAI:SearchServiceEndpoint", searchEndpoint },
            { "AzureAI:DocumentIntelligenceEndpoint", documentIntelligenceEndpoint },
            { "AzureAI:FoundryEndpoint", foundryEndpoint }
        };

        var azureAISection = new ConfigurationBuilder()
            .AddInMemoryCollection(azureAIConfig)
            .Build();

        // Merge environment-based config into the existing configuration
        foreach (var kvp in azureAISection.AsEnumerable().Where(x => x.Value != null))
        {
            ((IConfigurationBuilder)configuration).AddInMemoryCollection(new[] { kvp });
        }
    }

    /// <summary>
    /// Validates a single Azure service endpoint.
    /// </summary>
    private static void ValidateEndpoint(string serviceName, string endpoint, string envVarName, bool isProduction)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(
                $"Azure {serviceName} endpoint is not configured. " +
                $"Set the {envVarName} environment variable. " +
                $"For local development, use 'dotnet user-secrets set \"{envVarName}\" \"https://your-{serviceName.ToLower()}-endpoint.com/\"'. " +
                $"Endpoint must be a valid HTTPS URL.");
        }

        // Verify endpoint is HTTPS
        if (!endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Azure {serviceName} endpoint must use HTTPS protocol. " +
                $"Current endpoint: {endpoint}. " +
                $"Set a valid HTTPS URL in the {envVarName} environment variable.");
        }

        // Verify endpoint is a valid URI
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https")
        {
            throw new InvalidOperationException(
                $"Azure {serviceName} endpoint is not a valid HTTPS URL. " +
                $"Current endpoint: {endpoint}. " +
                $"Verify the URL is properly formatted in the {envVarName} environment variable.");
        }
    }
}