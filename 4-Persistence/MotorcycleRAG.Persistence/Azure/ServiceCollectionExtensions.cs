using Azure.Identity;
using AzureSearchClient = Azure.Search.Documents.SearchClient;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Resilience;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Search;
using MotorcycleRAG.Persistence.Telemetry;
using MotorcycleRAG.Persistence.Azure.Blob;
using MotorcycleRAG.Persistence.Azure.Search;
using MotorcycleRAG.Core.Options;
using Polly;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Extension methods for registering Azure services in DI container
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S1200:Split this class into smaller and more specialized ones", Justification = "DI registration class naturally has many dependencies")]
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Register all Azure service clients with authentication and resilience patterns
    /// </summary>
    public static IServiceCollection AddAzureServices(
        this IServiceCollection services,
        IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        // Configure options from appsettings
        services.Configure<AzureFoundryOptions>(
            configuration.GetSection("AzureAI"));
        services.Configure<SearchOptions>(
            configuration.GetSection("Search"));
        services.Configure<TelemetryOptions>(
            configuration.GetSection("ApplicationInsights"));
        services.Configure<ResilienceOptions>(
            configuration.GetSection("Resilience"));
        services.Configure<IngestionOptions>(
            configuration.GetSection("Ingestion"));
        services.Configure<BlobStorageOptions>(
            configuration.GetSection("BlobStorage"));

        // Validate configuration on startup
        // AzureFoundryOptions and SearchOptions validation is owned by the API layer
        // (AzureAIConfigurationValidator / SearchConfigurationValidator) to avoid duplicate
        // validators that run with different strictness levels.
        services.AddSingleton<IValidateOptions<ResilienceOptions>, ResilienceConfigurationValidator>();

        // Register resilience services as singletons
        services.AddSingleton<IResilienceService, MotorcycleRAG.Persistence.Resilience.ResilienceService>();
        services.AddSingleton<ICorrelationService, MotorcycleRAG.Persistence.Resilience.CorrelationService>();

        // Register telemetry and budget monitoring services
        services.AddSingleton<ITelemetryService, TelemetryService>();
        services.AddSingleton<IBudgetMonitorService, BudgetMonitorService>();

        // Register blob-backed asset store
        services.AddScoped<IManualPageAssetStore, BlobManualPageAssetStore>();

        // Register Azure service clients (scope aligns with dependencies)
        services.AddSingleton<IAzureFoundryClient>(serviceProvider => {
            var options = serviceProvider.GetRequiredService<IOptions<AzureFoundryOptions>>();
            var logger = serviceProvider.GetRequiredService<ILogger<MotorcycleRAG.Persistence.Azure.AzureFoundryClientWrapper>>();
            var resilience = serviceProvider.GetRequiredService<IResilienceService>();
            var correlation = serviceProvider.GetRequiredService<ICorrelationService>();
            var httpFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var config = serviceProvider.GetRequiredService<IConfiguration>();

            return new MotorcycleRAG.Persistence.Azure.AzureFoundryClientWrapper(
                options, logger, resilience, correlation, httpFactory, config);
        });
        services.AddScoped<IAzureSearchClient, MotorcycleRAG.Persistence.Azure.AzureSearchClientWrapper>();
        if (HasDocumentIntelligenceEndpoint(configuration)) {
            services.AddSingleton<IDocumentIntelligenceClient, MotorcycleRAG.Persistence.Azure.DocumentIntelligenceClientWrapper>();
        }
        else {
            services.AddSingleton<IDocumentIntelligenceClient, DisabledDocumentIntelligenceClient>();
        }
        
        // Register extracted Azure Search services
        services.AddScoped<MotorcycleRAG.Contracts.Interfaces.IAzureSearchQueryService, MotorcycleRAG.Persistence.Azure.Search.AzureSearchQueryService>();
        services.AddScoped<MotorcycleRAG.Contracts.Interfaces.IAzureSearchDocumentService, MotorcycleRAG.Persistence.Azure.Search.AzureSearchDocumentService>();
        services.AddScoped<MotorcycleRAG.Contracts.Interfaces.IAzureSearchHealthService, MotorcycleRAG.Persistence.Azure.Search.AzureSearchHealthService>();

        // Register SearchIndexClient for direct Azure Search operations
        services.AddSingleton<SearchIndexClient>(serviceProvider => {
            var azureConfig = serviceProvider.GetRequiredService<IOptions<AzureFoundryOptions>>().Value;
            var credential = new DefaultAzureCredential();
            return new SearchIndexClient(new Uri(azureConfig.SearchServiceEndpoint), credential);
        });

        services.AddSingleton<AzureSearchClient>(serviceProvider => {
            var azureConfig = serviceProvider.GetRequiredService<IOptions<AzureFoundryOptions>>().Value;
            var searchConfig = serviceProvider.GetRequiredService<IOptions<SearchOptions>>().Value;
            var credential = new DefaultAzureCredential();
            return new AzureSearchClient(new Uri(azureConfig.SearchServiceEndpoint), searchConfig.IndexName, credential);
        });

        // Register indexing services
        services.AddScoped<IMotorcycleIndexingService, MotorcycleIndexingService>();
        if (string.Equals(configuration["Search:ChunkIndexingProvider"], "InMemoryShim", StringComparison.OrdinalIgnoreCase)) {
            services.AddHttpClient<InMemorySearchShimChunkIndexingService>();
            services.AddScoped<IChunkIndexingService, InMemorySearchShimChunkIndexingService>();
        }
        else {
            services.AddScoped<IChunkIndexingService, ChunkIndexingService>();
        }

        // Configure HTTP clients for external services
        // NOTE: Resilience policies (retry + circuit breaker) are applied in the Presentation layer
        // at API startup time via Polly.Extensions.Http (Program.cs and Configuration/*.cs)
        services.AddHttpClient();

        // Register SQL persistence services
        services.AddSqlPersistenceServices(configuration);

        return services;
    }

    private static bool HasDocumentIntelligenceEndpoint(IConfiguration configuration) =>
        Uri.TryCreate(configuration["AzureAI:DocumentIntelligenceEndpoint"], UriKind.Absolute, out _);

    public static IServiceCollection AddPipelineHttpClients(this IServiceCollection services) {
        services.AddTransient<HttpResilienceDelegatingHandler>();
        services.AddHttpClient("LocalPipelineService")
            .AddHttpMessageHandler<HttpResilienceDelegatingHandler>();
        services.AddHttpClient("FabricPipelineService")
            .AddHttpMessageHandler<HttpResilienceDelegatingHandler>();
        return services;
    }

    public static IServiceCollection AddWebSearchHttpClient(this IServiceCollection services) {
        services.AddTransient<HttpResilienceDelegatingHandler>();
        services.AddHttpClient("WebSearchAgent")
            .AddHttpMessageHandler<HttpResilienceDelegatingHandler>();
        return services;
    }
}

/// <summary>
/// Validates Resilience configuration on startup
/// </summary>
public class ResilienceConfigurationValidator : IValidateOptions<ResilienceOptions> {
    public ValidateOptionsResult Validate(string? name, ResilienceOptions options) {
        ArgumentNullException.ThrowIfNull(options);
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
