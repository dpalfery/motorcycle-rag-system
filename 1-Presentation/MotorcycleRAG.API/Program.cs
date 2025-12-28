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
using Microsoft.Identity.Web;
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

        // Add Application Insights telemetry
        var appInsightsConnectionString = configuration.GetConnectionString("ApplicationInsights")
            ?? configuration["ApplicationInsights:ConnectionString"];

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

        // Add services to the container
        builder.Services.AddControllers();

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

        // Configure CORS
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

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

            // Add authentication services
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

            // Add authorization policies for admin roles
            builder.Services.AddAuthorization(options =>
            {
                // Admin policy - requires Admin app role
                options.AddPolicy("Admin", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "Admin");
                });

                // DataAdmin policy - requires DataAdmin app role
                options.AddPolicy("DataAdmin", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "DataAdmin");
                });

                // ContentAdmin policy - requires ContentAdmin app role
                options.AddPolicy("ContentAdmin", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "ContentAdmin");
                });

                // SuperAdmin policy - requires SuperAdmin app role
                options.AddPolicy("SuperAdmin", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "SuperAdmin");
                });

                // User policy - requires User app role (basic authenticated user)
                options.AddPolicy("User", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "User");
                });

                // Viewer policy - requires Viewer app role (read-only access)
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
        app.UseSecurityHeaders();
        app.UseCorrelationId();
        app.UseRateLimiter();
        app.UseExceptionHandling();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorizationLogging(); // Add authorization logging middleware
        app.UseAuthorization();

        // Map controllers and health checks
        app.MapControllers();
        app.MapHealthChecks("/health");

        // Log startup information
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Motorcycle RAG API starting up...");
        logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

        app.Run();
    }
}