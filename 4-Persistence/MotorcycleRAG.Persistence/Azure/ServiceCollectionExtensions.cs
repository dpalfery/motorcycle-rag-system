using Azure.Identity;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Agents;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using MotorcycleRAG.Infrastructure.Resilience;
using MotorcycleRAG.Infrastructure.Search;

namespace MotorcycleRAG.Infrastructure.Azure;

/// <summary>
/// Extension methods for registering Azure services in DI container
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register all Azure service clients with authentication and resilience patterns
    /// </summary>
    public static IServiceCollection AddAzureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure options from appsettings
        services.Configure<AzureAIConfiguration>(
            configuration.GetSection("AzureAI"));
        services.Configure<SearchConfiguration>(
            configuration.GetSection("Search"));
        services.Configure<TelemetryConfiguration>(
            configuration.GetSection("ApplicationInsights"));
        services.Configure<ResilienceConfiguration>(
            configuration.GetSection("Resilience"));

        // Validate configuration on startup
        services.AddSingleton<IValidateOptions<AzureAIConfiguration>, AzureAIConfigurationValidator>();
        services.AddSingleton<IValidateOptions<SearchConfiguration>, SearchConfigurationValidator>();
        services.AddSingleton<IValidateOptions<ResilienceConfiguration>, ResilienceConfigurationValidator>();

        // Register resilience services as singletons
        services.AddSingleton<IResilienceService, ResilienceService>();
        services.AddSingleton<ICorrelationService, CorrelationService>();

        // Register Azure service clients as singletons for connection pooling
        services.AddSingleton<IAzureOpenAIClient, AzureOpenAIClientWrapper>();
        services.AddSingleton<IAzureSearchClient, AzureSearchClientWrapper>();
        services.AddSingleton<IDocumentIntelligenceClient, DocumentIntelligenceClientWrapper>();

        // Register SearchIndexClient for direct Azure Search operations
        services.AddSingleton<SearchIndexClient>(serviceProvider =>
        {
            var azureConfig = serviceProvider.GetRequiredService<IOptions<AzureAIConfiguration>>().Value;
            var credential = new DefaultAzureCredential();
            return new SearchIndexClient(new Uri(azureConfig.SearchServiceEndpoint), credential);
        });

        // Register indexing service
        services.AddScoped<IMotorcycleIndexingService, MotorcycleIndexingService>();

        // Register search agents
        services.AddScoped<ISearchAgent, VectorSearchAgent>();

        // Configure HTTP clients for external services
        services.AddHttpClient();

        return services;
    }
}

/// <summary>
/// Validates Azure AI configuration on startup
/// </summary>
public class AzureAIConfigurationValidator : IValidateOptions<AzureAIConfiguration>
{
    public ValidateOptionsResult Validate(string? name, AzureAIConfiguration options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.FoundryEndpoint))
            failures.Add("AzureAI:FoundryEndpoint is required");

        if (string.IsNullOrWhiteSpace(options.OpenAIEndpoint))
            failures.Add("AzureAI:OpenAIEndpoint is required");

        if (string.IsNullOrWhiteSpace(options.SearchServiceEndpoint))
            failures.Add("AzureAI:SearchServiceEndpoint is required");

        if (string.IsNullOrWhiteSpace(options.DocumentIntelligenceEndpoint))
            failures.Add("AzureAI:DocumentIntelligenceEndpoint is required");

        if (!Uri.TryCreate(options.FoundryEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:FoundryEndpoint must be a valid URI");

        if (!Uri.TryCreate(options.OpenAIEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:OpenAIEndpoint must be a valid URI");

        if (!Uri.TryCreate(options.SearchServiceEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:SearchServiceEndpoint must be a valid URI");

        if (!Uri.TryCreate(options.DocumentIntelligenceEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:DocumentIntelligenceEndpoint must be a valid URI");

        if (options.Models.MaxTokens <= 0)
            failures.Add("AzureAI:Models:MaxTokens must be greater than 0");

        if (options.Models.Temperature < 0 || options.Models.Temperature > 2)
            failures.Add("AzureAI:Models:Temperature must be between 0 and 2");

        if (options.Retry.MaxRetries <= 0)
            failures.Add("AzureAI:Retry:MaxRetries must be greater than 0");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Validates Search configuration on startup
/// </summary>
public class SearchConfigurationValidator : IValidateOptions<SearchConfiguration>
{
    public ValidateOptionsResult Validate(string? name, SearchConfiguration options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.IndexName))
            failures.Add("Search:IndexName is required");

        if (options.BatchSize <= 0)
            failures.Add("Search:BatchSize must be greater than 0");

        if (options.MaxSearchResults <= 0)
            failures.Add("Search:MaxSearchResults must be greater than 0");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Validates Resilience configuration on startup
/// </summary>
public class ResilienceConfigurationValidator : IValidateOptions<ResilienceConfiguration>
{
    public ValidateOptionsResult Validate(string? name, ResilienceConfiguration options)
    {
        var failures = new List<string>();

        // Validate circuit breaker configurations
        if (options.CircuitBreaker.OpenAI.FailureThreshold <= 0)
            failures.Add("Resilience:CircuitBreaker:OpenAI:FailureThreshold must be greater than 0");

        if (options.CircuitBreaker.Search.FailureThreshold <= 0)
            failures.Add("Resilience:CircuitBreaker:Search:FailureThreshold must be greater than 0");

        if (options.CircuitBreaker.DocumentIntelligence.FailureThreshold <= 0)
            failures.Add("Resilience:CircuitBreaker:DocumentIntelligence:FailureThreshold must be greater than 0");

        // Validate retry configuration
        if (options.Retry.MaxRetries <= 0)
            failures.Add("Resilience:Retry:MaxRetries must be greater than 0");

        if (options.Retry.BaseDelaySeconds <= 0)
            failures.Add("Resilience:Retry:BaseDelaySeconds must be greater than 0");

        if (options.Retry.MaxDelaySeconds <= 0)
            failures.Add("Resilience:Retry:MaxDelaySeconds must be greater than 0");

        // Validate fallback configuration
        if (options.Fallback.CacheExpiration <= TimeSpan.Zero)
            failures.Add("Resilience:Fallback:CacheExpiration must be greater than zero");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}