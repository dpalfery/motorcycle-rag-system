using MotorcycleRAG.API.Configuration;
using Microsoft.ApplicationInsights.Extensibility;
using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Azure.AppConfiguration.AspNetCore;

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
builder.Host.ConfigureAppConfiguration((hostingContext, config) =>
{
    var tempConfig = config.Build();
    var appConfigEndpoint = tempConfig["AppConfig:Endpoint"];
    if (!string.IsNullOrEmpty(appConfigEndpoint))
    {
        var credential = new DefaultAzureCredential();
        config.AddAzureAppConfiguration(options =>
        {
            options.Connect(new Uri(appConfigEndpoint), credential)
                   // Load all non-labelled keys
                   .Select(KeyFilter.Any, LabelFilter.Null)
                   // Load environment-specific labelled keys (e.g. Development, Production)
                   .Select(KeyFilter.Any, hostingContext.HostingEnvironment.EnvironmentName)
                   // Configure Key Vault integration
                   .ConfigureKeyVault(kv => kv.SetCredential(credential))
                   // Configure refresh with sentinel key for live configuration updates
                   .ConfigureRefresh(refreshOptions =>
                   {
                       // When the sentinel key changes, refresh all cached configuration values
                       refreshOptions.Register("Settings:Sentinel", refreshAll: true)
                                     .SetCacheExpiration(TimeSpan.FromSeconds(30));
                   });
        });
    }
});

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
builder.Services.Configure<MotorcycleRAG.Core.Models.AppConfiguration>(configuration);

// Consumers are encouraged to depend on IOptionsMonitor<AppConfiguration> so they receive live updates when the
// sentinel key changes in Azure App Configuration.

// Configure API documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { 
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
    builder.Services.AddHealthChecks(configuration);
    
    // Validate configuration early
    ValidateConfiguration(configuration, builder.Environment);
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
app.UseCors();
app.UseAuthorization();

// Map controllers and health checks
app.MapControllers();
app.MapHealthChecks("/health");

// Log startup information
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Motorcycle RAG API starting up...");
logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

app.Run();

/// <summary>
/// Validate configuration during startup
/// </summary>
static void ValidateConfiguration(IConfiguration configuration, IWebHostEnvironment environment)
{
    var errors = new List<string>();

    // Validate Azure AI configuration
    var azureSection = configuration.GetSection("AzureAI");
    if (!azureSection.Exists())
    {
        errors.Add("AzureAI configuration section is missing");
    }
    else
    {
        ValidateRequiredSetting(azureSection, "FoundryEndpoint", errors);
        ValidateRequiredSetting(azureSection, "OpenAIEndpoint", errors);
        ValidateRequiredSetting(azureSection, "SearchServiceEndpoint", errors);
        ValidateRequiredSetting(azureSection, "DocumentIntelligenceEndpoint", errors);
        
        var modelsSection = azureSection.GetSection("Models");
        if (!modelsSection.Exists())
        {
            errors.Add("AzureAI:Models configuration section is missing");
        }
        else
        {
            ValidateRequiredSetting(modelsSection, "ChatModel", errors);
            ValidateRequiredSetting(modelsSection, "EmbeddingModel", errors);
            ValidateRequiredSetting(modelsSection, "QueryPlannerModel", errors);
            ValidateRequiredSetting(modelsSection, "VisionModel", errors);
        }
    }

    // Validate Search configuration
    var searchSection = configuration.GetSection("Search");
    if (!searchSection.Exists())
    {
        errors.Add("Search configuration section is missing");
    }
    else
    {
        ValidateRequiredSetting(searchSection, "IndexName", errors);
    }

    // Validate Application Insights configuration (only in production)
    if (environment.IsProduction())
    {
        var appInsightsConnectionString = configuration.GetConnectionString("ApplicationInsights") 
            ?? configuration["ApplicationInsights:ConnectionString"];
        
        if (string.IsNullOrWhiteSpace(appInsightsConnectionString))
        {
            errors.Add("Application Insights connection string is required in production environment");
        }
    }
    else
    {
        // In development, just warn if Application Insights is not configured
        var appInsightsConnectionString = configuration.GetConnectionString("ApplicationInsights") 
            ?? configuration["ApplicationInsights:ConnectionString"];
        
        if (string.IsNullOrWhiteSpace(appInsightsConnectionString))
        {
            Console.WriteLine("Warning: Application Insights connection string is not configured for development environment");
        }
    }

    if (errors.Count > 0)
    {
        var errorMessage = $"Configuration validation failed:\n{string.Join("\n", errors.Select(e => $"- {e}"))}";
        throw new InvalidOperationException(errorMessage);
    }
}

/// <summary>
/// Validate a required configuration setting
/// </summary>
static void ValidateRequiredSetting(IConfigurationSection section, string key, List<string> errors)
{
    var value = section[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        errors.Add($"{section.Path}:{key} is required but not configured");
    }
}