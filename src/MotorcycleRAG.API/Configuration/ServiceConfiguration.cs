using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net.NetworkInformation;

namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Extension methods for configuring services in the DI container
/// </summary>
public static class ServiceConfiguration
{
    /// <summary>
    /// Configure Azure AI services
    /// </summary>
    public static IServiceCollection AddAzureAIServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure Azure AI settings with validation
        services.Configure<AzureAIConfiguration>(configuration.GetSection("AzureAI"));
        services.Configure<SearchConfiguration>(configuration.GetSection("Search"));
        services.Configure<TelemetryConfiguration>(configuration.GetSection("ApplicationInsights"));

        // Add options validation
        services.AddSingleton<IValidateOptions<AzureAIConfiguration>, AzureAIConfigurationValidator>();
        services.AddSingleton<IValidateOptions<SearchConfiguration>, SearchConfigurationValidator>();
        services.AddSingleton<IValidateOptions<TelemetryConfiguration>, TelemetryConfigurationValidator>();

        // Register Azure service clients (will be implemented in Infrastructure layer)
        // services.AddSingleton<IAzureOpenAIClient, AzureOpenAIClient>();
        // services.AddSingleton<ISearchClient, AzureSearchClient>();
        // services.AddSingleton<IDocumentIntelligenceClient, DocumentIntelligenceClient>();

        return services;
    }

    /// <summary>
    /// Configure core application services
    /// </summary>
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // Register core service interfaces (implementations will be added later)
        // services.AddScoped<IMotorcycleRAGService, MotorcycleRAGService>();
        // services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();

        return services;
    }

    /// <summary>
    /// Configure search agents
    /// </summary>
    public static IServiceCollection AddSearchAgents(this IServiceCollection services)
    {
        // Register search agent implementations (will be implemented later)
        // services.AddScoped<ISearchAgent, VectorSearchAgent>();
        // services.AddScoped<ISearchAgent, WebSearchAgent>();
        // services.AddScoped<ISearchAgent, PDFSearchAgent>();
        // services.AddScoped<ISearchAgent, QueryPlannerAgent>();

        return services;
    }

    /// <summary>
    /// Configure data processors
    /// </summary>
    public static IServiceCollection AddDataProcessors(this IServiceCollection services)
    {
        // Register data processor implementations (will be implemented later)
        // services.AddScoped<IDataProcessor<CSVFile>, MotorcycleCSVProcessor>();
        // services.AddScoped<IDataProcessor<PDFDocument>, MotorcyclePDFProcessor>();

        return services;
    }

    /// <summary>
    /// Configure health checks
    /// </summary>
    public static IServiceCollection AddHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var healthChecksBuilder = services.AddHealthChecks()
            .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("API is running"))
            .AddCheck("configuration", () => ValidateConfiguration(configuration));

        // Add Azure service health checks if endpoints are configured
        var azureConfig = configuration.GetSection("AzureAI");
        
        var openAIEndpoint = azureConfig["OpenAIEndpoint"];
        if (!string.IsNullOrWhiteSpace(openAIEndpoint) && Uri.TryCreate(openAIEndpoint, UriKind.Absolute, out var openAIUri))
        {
            healthChecksBuilder.AddCheck("azure-openai-config", () => 
                ValidateEndpointConfiguration("Azure OpenAI", openAIEndpoint));
        }

        var searchEndpoint = azureConfig["SearchServiceEndpoint"];
        if (!string.IsNullOrWhiteSpace(searchEndpoint) && Uri.TryCreate(searchEndpoint, UriKind.Absolute, out var searchUri))
        {
            healthChecksBuilder.AddCheck("azure-search-config", () => 
                ValidateEndpointConfiguration("Azure Search", searchEndpoint));
        }

        var documentIntelligenceEndpoint = azureConfig["DocumentIntelligenceEndpoint"];
        if (!string.IsNullOrWhiteSpace(documentIntelligenceEndpoint) && Uri.TryCreate(documentIntelligenceEndpoint, UriKind.Absolute, out var docUri))
        {
            healthChecksBuilder.AddCheck("azure-document-intelligence-config", () => 
                ValidateEndpointConfiguration("Azure Document Intelligence", documentIntelligenceEndpoint));
        }

        return services;
    }

    /// <summary>
    /// Validate overall configuration health
    /// </summary>
    private static Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult ValidateConfiguration(IConfiguration configuration)
    {
        try
        {
            var issues = new List<string>();

            // Check required configuration sections
            var azureSection = configuration.GetSection("AzureAI");
            if (!azureSection.Exists())
                issues.Add("AzureAI configuration section is missing");

            var searchSection = configuration.GetSection("Search");
            if (!searchSection.Exists())
                issues.Add("Search configuration section is missing");

            // Check Application Insights configuration
            var appInsightsConnectionString = configuration.GetConnectionString("ApplicationInsights") 
                ?? configuration["ApplicationInsights:ConnectionString"];
            
            if (string.IsNullOrWhiteSpace(appInsightsConnectionString))
                issues.Add("Application Insights connection string is not configured");

            return issues.Count == 0 
                ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("All configuration sections are present")
                : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded($"Configuration issues: {string.Join(", ", issues)}");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Configuration validation failed", ex);
        }
    }

    /// <summary>
    /// Validate Azure service endpoint configuration
    /// </summary>
    private static Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult ValidateEndpointConfiguration(string serviceName, string endpoint)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy($"{serviceName} endpoint is not configured");

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy($"{serviceName} endpoint is not a valid URL: {endpoint}");

            if (uri.Scheme != "https")
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded($"{serviceName} endpoint should use HTTPS: {endpoint}");

            // Check if endpoint looks like a placeholder
            if (endpoint.Contains("your-") || endpoint.Contains("example") || endpoint.Contains("placeholder"))
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded($"{serviceName} endpoint appears to be a placeholder: {endpoint}");

            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy($"{serviceName} endpoint is properly configured");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy($"{serviceName} endpoint validation failed", ex);
        }
    }
}

/// <summary>
/// Validator for Azure AI configuration
/// </summary>
public class AzureAIConfigurationValidator : IValidateOptions<AzureAIConfiguration>
{
    public ValidateOptionsResult Validate(string? name, AzureAIConfiguration options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.FoundryEndpoint))
            failures.Add("AzureAI:FoundryEndpoint is required");
        else if (!Uri.TryCreate(options.FoundryEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:FoundryEndpoint must be a valid URL");

        if (string.IsNullOrWhiteSpace(options.OpenAIEndpoint))
            failures.Add("AzureAI:OpenAIEndpoint is required");
        else if (!Uri.TryCreate(options.OpenAIEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:OpenAIEndpoint must be a valid URL");

        if (string.IsNullOrWhiteSpace(options.SearchServiceEndpoint))
            failures.Add("AzureAI:SearchServiceEndpoint is required");
        else if (!Uri.TryCreate(options.SearchServiceEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:SearchServiceEndpoint must be a valid URL");

        if (string.IsNullOrWhiteSpace(options.DocumentIntelligenceEndpoint))
            failures.Add("AzureAI:DocumentIntelligenceEndpoint is required");
        else if (!Uri.TryCreate(options.DocumentIntelligenceEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:DocumentIntelligenceEndpoint must be a valid URL");

        if (options.Models == null)
            failures.Add("AzureAI:Models configuration is required");
        else
        {
            if (string.IsNullOrWhiteSpace(options.Models.ChatModel))
                failures.Add("AzureAI:Models:ChatModel is required");
            if (string.IsNullOrWhiteSpace(options.Models.EmbeddingModel))
                failures.Add("AzureAI:Models:EmbeddingModel is required");
            if (options.Models.MaxTokens <= 0)
                failures.Add("AzureAI:Models:MaxTokens must be greater than 0");
            if (options.Models.Temperature < 0 || options.Models.Temperature > 2)
                failures.Add("AzureAI:Models:Temperature must be between 0 and 2");
        }

        return failures.Count > 0 
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Validator for Search configuration
/// </summary>
public class SearchConfigurationValidator : IValidateOptions<SearchConfiguration>
{
    public ValidateOptionsResult Validate(string? name, SearchConfiguration options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.IndexName))
            failures.Add("Search:IndexName is required");

        if (options.BatchSize <= 0 || options.BatchSize > 1000)
            failures.Add("Search:BatchSize must be between 1 and 1000");

        if (options.MaxSearchResults <= 0 || options.MaxSearchResults > 100)
            failures.Add("Search:MaxSearchResults must be between 1 and 100");

        return failures.Count > 0 
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Validator for Telemetry configuration
/// </summary>
public class TelemetryConfigurationValidator : IValidateOptions<TelemetryConfiguration>
{
    public ValidateOptionsResult Validate(string? name, TelemetryConfiguration options)
    {
        var failures = new List<string>();

        if (options.EnableTelemetry && string.IsNullOrWhiteSpace(options.ConnectionString))
            failures.Add("ApplicationInsights:ConnectionString is required when telemetry is enabled");

        if (string.IsNullOrWhiteSpace(options.ApplicationName))
            failures.Add("ApplicationInsights:ApplicationName is required");

        return failures.Count > 0 
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}