using Azure.Identity;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Resilience;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Search;

using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Extension methods for registering Azure services in DI container
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Register all Azure service clients with authentication and resilience patterns
    /// </summary>
    public static IServiceCollection AddAzureServices(
        this IServiceCollection services,
        IConfiguration configuration) {
        // Configure options from appsettings
        services.Configure<AzureAIOptions>(
            configuration.GetSection("AzureAI"));
        services.Configure<SearchOptions>(
            configuration.GetSection("Search"));
        services.Configure<TelemetryOptions>(
            configuration.GetSection("ApplicationInsights"));
        services.Configure<ResilienceOptions>(
            configuration.GetSection("Resilience"));

        // Validate configuration on startup
        services.AddSingleton<IValidateOptions<AzureAIOptions>, AzureAIConfigurationValidator>();
        services.AddSingleton<IValidateOptions<SearchOptions>, SearchConfigurationValidator>();
        services.AddSingleton<IValidateOptions<ResilienceOptions>, ResilienceConfigurationValidator>();

        // Register resilience services as singletons
        services.AddSingleton<IResilienceService, MotorcycleRAG.Persistence.Resilience.ResilienceService>();
        services.AddSingleton<ICorrelationService, MotorcycleRAG.Persistence.Resilience.CorrelationService>();

        // Register Azure service clients as singletons for connection pooling
        services.AddSingleton<IAzureOpenAIClient, MotorcycleRAG.Persistence.Azure.AzureOpenAIClientWrapper>();
        services.AddSingleton<IAzureSearchClient, MotorcycleRAG.Persistence.Azure.AzureSearchClientWrapper>();
        services.AddSingleton<IDocumentIntelligenceClient, MotorcycleRAG.Persistence.Azure.DocumentIntelligenceClientWrapper>();

        // Register SearchIndexClient for direct Azure Search operations
        services.AddSingleton<SearchIndexClient>(serviceProvider => {
            var azureConfig = serviceProvider.GetRequiredService<IOptions<AzureAIOptions>>().Value;
            var credential = new DefaultAzureCredential();
            return new SearchIndexClient(new Uri(azureConfig.SearchServiceEndpoint), credential);
        });

        // Register indexing service
        services.AddScoped<IMotorcycleIndexingService, MotorcycleIndexingService>();

        // Configure HTTP clients for external services
        services.AddHttpClient();

        // Register SQL persistence services
        services.AddSqlPersistenceServices(configuration);

        return services;
    }
}

/// <summary>
/// Validates Azure AI configuration on startup
/// </summary>
public class AzureAIConfigurationValidator : IValidateOptions<AzureAIOptions> {
    public ValidateOptionsResult Validate(string? name, AzureAIOptions options) {
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
public class SearchConfigurationValidator : IValidateOptions<SearchOptions> {
    public ValidateOptionsResult Validate(string? name, SearchOptions options) {
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
public class ResilienceConfigurationValidator : IValidateOptions<ResilienceOptions> {
    public ValidateOptionsResult Validate(string? name, ResilienceOptions options) {
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
