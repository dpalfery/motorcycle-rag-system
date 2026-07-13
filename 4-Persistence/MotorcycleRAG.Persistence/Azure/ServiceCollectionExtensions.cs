using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Local;
using MotorcycleRAG.Persistence.Resilience;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Search;
using MotorcycleRAG.Persistence.Telemetry;
using MotorcycleRAG.Persistence.Web;
using MotorcycleRAG.Persistence.Azure.Blob;
using MotorcycleRAG.Persistence.Azure.Search;
using MotorcycleRAG.Persistence.DataProcessing;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.ExternalServices;
using MotorcycleRAG.Persistence.Notifications;
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
        services.AddOptions<CSVProcessingConfiguration>()
            .Bind(configuration.GetSection("CsvProcessing"))
            .ValidateOnStart();

        // Validate configuration on startup
        // AzureFoundryOptions and SearchOptions validation is owned by the API layer
        // (AzureAIConfigurationValidator / SearchConfigurationValidator) to avoid duplicate
        // validators that run with different strictness levels.
        services.AddSingleton<IValidateOptions<ResilienceOptions>, ResilienceConfigurationValidator>();
        services.AddSingleton<IValidateOptions<CSVProcessingConfiguration>, CsvProcessingConfigurationValidator>();

        // Register resilience services as singletons
        services.AddSingleton<IResilienceService, MotorcycleRAG.Persistence.Resilience.ResilienceService>();
        services.AddSingleton<ICorrelationService, MotorcycleRAG.Persistence.Resilience.CorrelationService>();

        // Register telemetry and budget monitoring services
        services.AddSingleton<ITelemetryService, TelemetryService>();
        services.AddSingleton<IBudgetMonitorService, BudgetMonitorService>();

        // Register Azure SDK client factories (singletons; stateless SDK client builders).
        // IBlobServiceClientFactory is consumed by AzureBlobStorageService and
        // BlobManualPageAssetStore; IFoundryClientFactory by FoundryAgentRunner. Both are
        // safe singletons — they only construct Azure SDK clients and hold no per-request state.
        services.AddSingleton<IBlobServiceClientFactory, BlobServiceClientFactory>();
        services.AddSingleton<IFoundryClientFactory, FoundryClientFactory>();
        services.AddSingleton<IAzureCredentialProvider, AzureCredentialProvider>();
        services.AddSingleton<ILocalFileStore, LocalFileStore>();
        services.AddSingleton<ILocalFileDiscovery, LocalFileDiscovery>();
        services.AddSingleton<IIngestionTelemetryService, IngestionTelemetryService>();
        services.AddScoped<ILocalPipelineService, LocalPipelineService>();

        // Register blob-backed asset store
        services.AddScoped<IManualPageAssetStore, BlobManualPageAssetStore>();
        services.AddScoped<IBlobStorageService, AzureBlobStorageService>();
        services.AddScoped<IApproverNotificationService, ApproverNotificationService>();
        services.Configure<ExternalIdentityProvisioningOptions>(
            configuration.GetSection("ExternalIdentityProvisioning"));
        services.AddHttpClient<IExternalIdentityProvisioningService, ExternalIdentityProvisioningService>();

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
        services.AddSingleton<IDocumentIntelligenceClient, DisabledDocumentIntelligenceClient>();
        
        // Register extracted Azure Search services
        services.AddScoped<MotorcycleRAG.Contracts.Interfaces.IAzureSearchQueryService, MotorcycleRAG.Persistence.Azure.Search.AzureSearchQueryService>();
        services.AddScoped<MotorcycleRAG.Contracts.Interfaces.IAzureSearchDocumentService, MotorcycleRAG.Persistence.Azure.Search.AzureSearchDocumentService>();
        services.AddScoped<MotorcycleRAG.Contracts.Interfaces.IAzureSearchHealthService, MotorcycleRAG.Persistence.Azure.Search.AzureSearchHealthService>();

        // Register SearchIndexClient for direct Azure Search operations (service-level;
        // NOT index-bound, so a singleton is correct and does not violate the
        // "no singleton SearchClient bound to one index" rule).
        services.AddSingleton<SearchIndexClient>(serviceProvider => {
            var azureConfig = serviceProvider.GetRequiredService<IOptions<AzureFoundryOptions>>().Value;
            var credentialProvider = serviceProvider.GetRequiredService<IAzureCredentialProvider>();
            return new SearchIndexClient(new Uri(azureConfig.SearchServiceEndpoint), credentialProvider.GetSearchCredential());
        });

        // Per-index SearchClient factory (D4 category partitioning). Replaces the former
        // singleton SearchClient that was bound to a single index name at DI time. Every
        // concrete-SearchClient consumer (indexing, query, document, health) now resolves a
        // client per category through this factory.
        services.AddSingleton<ISearchClientFactory, SearchClientFactory>();

        // T7: resilience pipeline for the per-index SearchClient batch upload operations.
        // Transient-only retry (5xx / 429 / network); non-transient status codes (404 / 400 /
        // 401 / 403) and SearchIndexNotFoundException propagate immediately. The per-batch
        // timeout is enforced by the caller via a linked CancellationTokenSource bound to
        // SearchOptions.BatchIndexTimeoutSeconds.
        services.AddSingleton<ISearchIndexResiliencePipeline, SearchIndexResiliencePipelineProvider>();

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
        services.AddWebSearchHttpClient();

        // Classifier options + local OpenAI-compatible chat client (D7 category classifier, R6 prod reachability).
        services.AddClassifierServices(configuration);

        // Register SQL persistence services
        services.AddSqlPersistenceServices(configuration);

        return services;
    }

    /// <summary>
    /// Registers the D7 motorcycle category classifier's infrastructure dependencies:
    /// <see cref="ClassifierOptions"/> and the local <see cref="ILocalChatClient"/> (typed HttpClient).
    /// </summary>
    /// <remarks>
    /// The Application-layer <c>MotorcycleCategoryClassifier</c> service itself is registered separately
    /// in the Application layer's <c>AddMotorcycleCaching</c>/<c>AddApplicationServices</c> wiring.
    /// </remarks>
    public static IServiceCollection AddClassifierServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind ClassifierOptions to the "Classifier" config section.
        services.Configure<ClassifierOptions>(configuration.GetSection("Classifier"));

        // Named HttpClient for the local chat client. Timeout comes from ClassifierOptions.TimeoutSeconds
        // (default 30s). The client reads Endpoint/Model from IOptions<ClassifierOptions> at request time,
        // so config changes don't require re-registration.
        services.AddHttpClient(OpenAiCompatibleChatClient.HttpClientName, (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<ClassifierOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds > 0 ? opts.TimeoutSeconds : 30);
            client.DefaultRequestHeaders.Add("User-Agent", "MotorcycleRAG-Classifier/1.0");
        });

        services.AddScoped<ILocalChatClient, OpenAiCompatibleChatClient>();

        return services;
    }

    public static IServiceCollection AddPipelineHttpClients(this IServiceCollection services) {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<HttpResilienceDelegatingHandler>();
        services.AddHttpClient("LocalPipelineService")
            .AddHttpMessageHandler<HttpResilienceDelegatingHandler>();
        return services;
    }

    public static IServiceCollection AddWebSearchHttpClient(this IServiceCollection services) {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<HttpResilienceDelegatingHandler>();
        services.AddSingleton<IWebContentExtractor, HtmlWebContentExtractor>();
        services.AddHttpClient(TrustedWebContentFetcher.HttpClientName, client => {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MotorcycleRAGBot/1.0)");
            })
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler {
                MaxConnectionsPerServer = 3,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                UseCookies = false,
            })
            .AddHttpMessageHandler<HttpResilienceDelegatingHandler>();
        services.AddScoped<ITrustedWebContentFetcher>(serviceProvider => new TrustedWebContentFetcher(
            serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(TrustedWebContentFetcher.HttpClientName),
            serviceProvider.GetRequiredService<IWebContentExtractor>()));
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
