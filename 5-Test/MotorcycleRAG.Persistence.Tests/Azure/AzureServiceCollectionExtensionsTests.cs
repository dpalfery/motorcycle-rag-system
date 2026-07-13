using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Azure;
using MotorcycleRAG.Persistence.Azure.Search;
using MotorcycleRAG.Persistence.Web;
using Azure.Search.Documents.Indexes;

namespace MotorcycleRAG.Persistence.Tests.Azure;
public class AzureServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAzureServices_ShouldThrowArgumentNullException_WhenConfigurationIsNull()
    {
        var act = () => new ServiceCollection().AddAzureServices(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }
    [Fact]
    public void AddAzureServices_ShouldRegisterDisabledDocIntelligence()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new() { ["Sql:ConnectionString"] = "Server=.;Database=test" });
        services.AddAzureServices(config);
        services.Should().Contain(sd => sd.ServiceType == typeof(IDocumentIntelligenceClient) && sd.ImplementationType == typeof(DisabledDocumentIntelligenceClient));
    }
    [Fact]
    public void AddAzureServices_ShouldRegisterCoreServices()
    {
        var services = new ServiceCollection();
        services.AddAzureServices(BuildConfig(new() { ["Sql:ConnectionString"] = "Server=.;Database=test" }));
        services.Should().Contain(sd => sd.ServiceType == typeof(IResilienceService));
        services.Should().Contain(sd => sd.ServiceType == typeof(ICorrelationService));
        services.Should().Contain(sd => sd.ServiceType == typeof(ITelemetryService));
        services.Should().Contain(sd => sd.ServiceType == typeof(IBudgetMonitorService));
        services.Should().Contain(sd => sd.ServiceType == typeof(IAzureFoundryClient));
        services.Should().Contain(sd => sd.ServiceType == typeof(IAzureSearchClient));
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterOneSingletonCredentialProvider()
    {
        var services = new ServiceCollection();

        services.AddAzureServices(BuildConfig(new() { ["Sql:ConnectionString"] = "Server=.;Database=test" }));

        services.Count(sd => sd.ServiceType == typeof(IAzureCredentialProvider)).Should().Be(1);
        services.Should().Contain(sd => sd.ServiceType == typeof(IAzureCredentialProvider)
            && sd.ImplementationType == typeof(AzureCredentialProvider)
            && sd.Lifetime == ServiceLifetime.Singleton);
    }
    [Fact]
    public void AddAzureServices_ShouldRegisterOptions()
    {
        var services = new ServiceCollection();
        services.AddAzureServices(BuildConfig(new() { ["Sql:ConnectionString"] = "Server=.;Database=test" }));
        services.Should().Contain(sd => sd.ServiceType == typeof(IConfigureOptions<AzureFoundryOptions>));
        services.Should().Contain(sd => sd.ServiceType == typeof(IConfigureOptions<Core.Options.SearchOptions>));
        services.Should().Contain(sd => sd.ServiceType == typeof(IConfigureOptions<ResilienceOptions>));
        services.Should().Contain(sd => sd.ServiceType == typeof(IConfigureOptions<BlobStorageOptions>));
    }
    [Fact]
    public void AddPipelineHttpClients_ShouldThrowArgumentNullException_WhenServicesIsNull()
    {
        var act = () => ((IServiceCollection)null!).AddPipelineHttpClients();
        act.Should().Throw<ArgumentNullException>();
    }
    [Fact]
    public void AddPipelineHttpClients_ShouldRegisterHandler()
    {
        var services = new ServiceCollection();
        services.AddPipelineHttpClients();
        services.Should().Contain(sd => sd.ServiceType == typeof(HttpResilienceDelegatingHandler) && sd.Lifetime == ServiceLifetime.Transient);
    }

    [Fact]
    public void AddAzureServicesAndPipelineHttpClients_ShouldRegisterOneSharedResilienceHandler()
    {
        var services = new ServiceCollection();
        services.AddAzureServices(BuildConfig(new() { ["Sql:ConnectionString"] = "Server=.;Database=test" }));
        services.AddPipelineHttpClients();

        services.Count(descriptor => descriptor.ServiceType == typeof(HttpResilienceDelegatingHandler)).Should().Be(1);
    }
    [Fact]
    public void AddWebSearchHttpClient_ShouldThrowArgumentNullException_WhenServicesIsNull()
    {
        var act = () => ((IServiceCollection)null!).AddWebSearchHttpClient();
        act.Should().Throw<ArgumentNullException>();
    }
    [Fact]
    public void AddWebSearchHttpClient_ShouldRegisterOuterFetcherAndConfiguredNamedClient()
    {
        var services = new ServiceCollection();
        services.AddWebSearchHttpClient();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient(TrustedWebContentFetcher.HttpClientName);

        services.Should().Contain(sd => sd.ServiceType == typeof(HttpResilienceDelegatingHandler) && sd.Lifetime == ServiceLifetime.Transient);
        services.Should().Contain(sd => sd.ServiceType == typeof(IWebContentExtractor) && sd.ImplementationType == typeof(HtmlWebContentExtractor));
        services.Should().Contain(sd => sd.ServiceType == typeof(ITrustedWebContentFetcher) && sd.Lifetime == ServiceLifetime.Scoped);
        client.Timeout.Should().Be(TimeSpan.FromSeconds(10));
        client.DefaultRequestHeaders.UserAgent.ToString().Should().Be("Mozilla/5.0 (compatible; MotorcycleRAGBot/1.0)");
    }
    [Fact]
    public void AddClassifierServices_ShouldThrowArgumentNullException_WhenConfigurationIsNull()
    {
        var act = () => new ServiceCollection().AddClassifierServices(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }
    [Fact]
    public void AddClassifierServices_ShouldRegisterOptions()
    {
        var services = new ServiceCollection();
        services.AddClassifierServices(BuildConfig(new() { ["Classifier:TimeoutSeconds"] = "30" }));
        services.Should().Contain(sd => sd.ServiceType == typeof(IConfigureOptions<ClassifierOptions>));
    }
    [Fact]
    public void AddClassifierServices_ShouldRegisterLocalChatClient()
    {
        var services = new ServiceCollection();
        services.AddClassifierServices(BuildConfig(new() { ["Classifier:TimeoutSeconds"] = "30" }));
        services.Should().Contain(sd => sd.ServiceType == typeof(ILocalChatClient) && sd.Lifetime == ServiceLifetime.Scoped);
    }
    [Fact]
    public void ResilienceConfigurationValidator_ShouldReturnSuccess_ForValidOptions()
    {
        var result = new ResilienceConfigurationValidator().Validate(null, new ResilienceOptions
        {
            CircuitBreaker = new() { OpenAI = new() { FailureThreshold = 5 }, Search = new() { FailureThreshold = 5 }, DocumentIntelligence = new() { FailureThreshold = 5 } },
            Retry = new() { MaxRetries = 3, BaseDelaySeconds = 1, MaxDelaySeconds = 60 },
            Fallback = new() { CacheExpiration = TimeSpan.FromMinutes(10) }
        });
        result.Succeeded.Should().BeTrue();
    }
    [Fact]
    public void ResilienceConfigurationValidator_ShouldReturnFailure_WhenInvalid()
    {
        var result = new ResilienceConfigurationValidator().Validate(null, new ResilienceOptions
        {
            CircuitBreaker = new() { OpenAI = new() { FailureThreshold = 0 }, Search = new() { FailureThreshold = 0 }, DocumentIntelligence = new() { FailureThreshold = 0 } },
            Retry = new() { MaxRetries = 0, BaseDelaySeconds = 0, MaxDelaySeconds = 0 },
            Fallback = new() { CacheExpiration = TimeSpan.Zero }
        });
        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("FailureThreshold"));
        result.Failures.Should().Contain(f => f.Contains("MaxRetries"));
        result.Failures.Should().Contain(f => f.Contains("CacheExpiration"));
    }

    [Fact]
    public void ResilienceConfigurationValidator_ShouldThrowArgumentNullException_WhenOptionsIsNull()
    {
        var act = () => new ResilienceConfigurationValidator().Validate(null, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterInMemoryShimIndexing_WhenConfigured()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new()
        {
            ["Sql:ConnectionString"] = "Server=.;Database=test",
            ["Search:ChunkIndexingProvider"] = "InMemoryShim"
        });
        services.AddAzureServices(config);
        services.Should().Contain(sd => sd.ServiceType == typeof(IChunkIndexingService)
            && sd.ImplementationType == typeof(InMemorySearchShimChunkIndexingService));
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterDefaultIndexing_WhenShimNotConfigured()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new() { ["Sql:ConnectionString"] = "Server=.;Database=test" });
        services.AddAzureServices(config);
        services.Should().Contain(sd => sd.ServiceType == typeof(IChunkIndexingService)
            && sd.ImplementationType == typeof(ChunkIndexingService));
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterSearchClientFactory()
    {
        var services = new ServiceCollection();
        services.AddAzureServices(BuildConfig(new() { ["Sql:ConnectionString"] = "Server=.;Database=test" }));
        services.Should().Contain(sd => sd.ServiceType == typeof(ISearchClientFactory));
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterSearchIndexResiliencePipeline()
    {
        var services = new ServiceCollection();
        services.AddAzureServices(BuildConfig(new() { ["Sql:ConnectionString"] = "Server=.;Database=test" }));
        services.Should().Contain(sd => sd.ServiceType == typeof(ISearchIndexResiliencePipeline));
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterIngestionOptions()
    {
        var services = new ServiceCollection();
        services.AddAzureServices(BuildConfig(new() { ["Sql:ConnectionString"] = "Server=.;Database=test" }));
        services.Should().Contain(sd => sd.ServiceType == typeof(IConfigureOptions<IngestionOptions>));
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterManualPageAssetStore()
    {
        var services = new ServiceCollection();
        services.AddAzureServices(BuildConfig(new() { ["Sql:ConnectionString"] = "Server=.;Database=test" }));
        services.Should().Contain(sd => sd.ServiceType == typeof(IManualPageAssetStore)
            && sd.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterAzureSearchServices()
    {
        var services = new ServiceCollection();
        services.AddAzureServices(BuildConfig(new() { ["Sql:ConnectionString"] = "Server=.;Database=test" }));
        services.Should().Contain(sd => sd.ServiceType == typeof(Contracts.Interfaces.IAzureSearchQueryService));
        services.Should().Contain(sd => sd.ServiceType == typeof(Contracts.Interfaces.IAzureSearchDocumentService));
        services.Should().Contain(sd => sd.ServiceType == typeof(Contracts.Interfaces.IAzureSearchHealthService));
        services.Should().Contain(sd => sd.ServiceType == typeof(IMotorcycleIndexingService));
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterRawSearchIndexClient()
    {
        var services = new ServiceCollection();
        services.AddLogging(); // required for ILogger resolution inside factory lambdas
        services.AddAzureServices(BuildConfig(new()
        {
            ["Sql:ConnectionString"] = "Server=.;Database=test",
            ["AzureAI:SearchServiceEndpoint"] = "https://search.example.com"
        }));

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(SearchIndexClient)
            && sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddClassifierServices_ShouldRegisterHttpClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddClassifierServices(BuildConfig(new()
        {
            ["Classifier:TimeoutSeconds"] = "15",
            ["Classifier:Endpoint"] = "http://localhost:11434"
        }));

        // Verify HttpClient for the local chat client is registered
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        factory.Should().NotBeNull();

        // Verify ILocalChatClient is registered with scoped lifetime
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ILocalChatClient)
            && sd.Lifetime == ServiceLifetime.Scoped);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> vals) =>
        new ConfigurationBuilder().AddInMemoryCollection(vals).Build();
}
