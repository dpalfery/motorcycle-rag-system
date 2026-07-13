using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Azure;
using MotorcycleRAG.Persistence.Azure.Search;

using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Azure;

public class ServiceCollectionExtensionsTests {
    private readonly IConfiguration _configuration;
    private readonly ServiceCollection _services;

    public ServiceCollectionExtensionsTests() {
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?> {
            ["AzureAI:FoundryEndpoint"] = "https://test-foundry.cognitiveservices.azure.com/",
            ["AzureAI:SearchServiceEndpoint"] = "https://test-search.search.windows.net/",
            ["AzureAI:DocumentIntelligenceEndpoint"] = "https://test-document-intelligence.cognitiveservices.azure.com/",
            ["AzureAI:Models:ChatModel"] = "gpt-4o-mini",
            ["AzureAI:Models:EmbeddingModel"] = "text-embedding-3-large",
            ["AzureAI:Models:QueryPlannerModel"] = "gpt-4o",
            ["AzureAI:Models:VisionModel"] = "gpt-4-vision-preview",
            ["AzureAI:Models:MaxTokens"] = "4096",
            ["AzureAI:Models:Temperature"] = "0.1",
            ["AzureAI:Models:TopP"] = "1.0",
            ["AzureAI:Retry:MaxRetries"] = "3",
            ["AzureAI:Retry:BaseDelaySeconds"] = "2",
            ["AzureAI:Retry:MaxDelaySeconds"] = "60",
            ["AzureAI:Retry:UseExponentialBackoff"] = "true",
            ["Search:IndexName"] = "test-index",
            ["Search:BatchSize"] = "100",
            ["Search:MaxSearchResults"] = "50",
            ["Search:EnableHybridSearch"] = "true",
            ["Search:EnableSemanticRanking"] = "true",
            ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=test-key",
            ["ApplicationInsights:EnableTelemetry"] = "true",
            ["ApplicationInsights:EnablePerformanceCounters"] = "true",
            ["ApplicationInsights:ApplicationName"] = "MotorcycleRAG"
        });
        _configuration = configurationBuilder.Build();
        _services = new ServiceCollection();
        _services.AddLogging();
        _services.AddSingleton<IConfiguration>(_configuration);
    }

    [Fact]
    public void AddAzureServices_WithValidConfiguration_ShouldRegisterAllServices() {
        // Act
        _services.AddAzureServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        serviceProvider.GetService<IAzureFoundryClient>().Should().NotBeNull();
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterConfigurationOptions() {
        // Act
        _services.AddAzureServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        var azureConfig = serviceProvider.GetService<IOptions<AzureFoundryOptions>>();
        azureConfig.Should().NotBeNull();
        azureConfig!.Value.FoundryEndpoint.Should().Be("https://test-foundry.cognitiveservices.azure.com/");

        var searchConfig = serviceProvider.GetService<IOptions<SearchOptions>>();
        searchConfig.Should().NotBeNull();
        searchConfig!.Value.IndexName.Should().Be("test-index");

        var telemetryConfig = serviceProvider.GetService<IOptions<TelemetryOptions>>();
        telemetryConfig.Should().NotBeNull();
        telemetryConfig!.Value.ApplicationName.Should().Be("MotorcycleRAG");
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterServicesAsSingletons() {
        // Act
        _services.AddAzureServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        var client1 = serviceProvider.GetService<IAzureFoundryClient>();
        var client2 = serviceProvider.GetService<IAzureFoundryClient>();

        client1.Should().BeSameAs(client2);
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterHttpClient() {
        // Act
        _services.AddAzureServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
        httpClientFactory.Should().NotBeNull();
    }

    [Fact]
    public void AddAzureServices_WithInMemoryShimProvider_ShouldRegisterShimChunkIndexer() {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["AzureAI:FoundryEndpoint"] = "https://test-foundry.cognitiveservices.azure.com/",
                ["AzureAI:SearchServiceEndpoint"] = "https://test-search.search.windows.net/",
                ["Search:IndexName"] = "test-index",
                ["Search:ChunkIndexingProvider"] = "InMemoryShim",
                ["Search:InMemoryShimEndpoint"] = "http://127.0.0.1:8765/"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddAzureServices(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        serviceProvider.GetRequiredService<IChunkIndexingService>()
            .Should()
            .BeOfType<InMemorySearchShimChunkIndexingService>();
    }

    [Fact]
    public void AddAzureServices_WithoutDocumentIntelligenceEndpoint_ShouldRegisterDisabledClient() {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["AzureAI:FoundryEndpoint"] = "https://test-foundry.cognitiveservices.azure.com/",
                ["AzureAI:SearchServiceEndpoint"] = "https://test-search.search.windows.net/",
                ["AzureAI:Models:ChatModel"] = "gpt-4o-mini",
                ["AzureAI:Models:EmbeddingModel"] = "text-embedding-3-large",
                ["AzureAI:Models:QueryPlannerModel"] = "gpt-4o",
                ["AzureAI:Models:VisionModel"] = "gpt-4-vision-preview",
                ["AzureAI:Models:MaxTokens"] = "4096",
                ["AzureAI:Models:Temperature"] = "0.1",
                ["AzureAI:Models:TopP"] = "1.0",
                ["AzureAI:Retry:MaxRetries"] = "3",
                ["AzureAI:Retry:BaseDelaySeconds"] = "2",
                ["AzureAI:Retry:MaxDelaySeconds"] = "60",
                ["AzureAI:Retry:UseExponentialBackoff"] = "true",
                ["Search:IndexName"] = "test-index",
                ["Search:BatchSize"] = "100",
                ["Search:MaxSearchResults"] = "50",
                ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=test-key",
                ["ApplicationInsights:EnableTelemetry"] = "true",
                ["ApplicationInsights:ApplicationName"] = "MotorcycleRAG"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddAzureServices(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        serviceProvider.GetRequiredService<IDocumentIntelligenceClient>()
            .Should()
            .BeOfType<DisabledDocumentIntelligenceClient>();
    }
}


public class SearchConfigurationValidatorTests {
    private readonly IValidateOptions<SearchOptions> _validator;

    public SearchConfigurationValidatorTests() {
        _validator = new SearchConfigurationValidator();
    }

    [Fact]
    public void Validate_WithValidConfiguration_ShouldReturnSuccess() {
        // Arrange
        var config = new SearchOptions {
            IndexName = "test-index",
            BatchSize = 100,
            MaxSearchResults = 50,
            EnableHybridSearch = true,
            EnableSemanticRanking = true
        };

        // Act
        var result = _validator.Validate(null, config);

        // Assert
        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Fact]
    public void Validate_WithInMemoryShimProviderAndEndpoint_ShouldReturnSuccess() {
        // Arrange
        var config = new SearchOptions {
            IndexName = "test-index",
            BatchSize = 100,
            MaxSearchResults = 50,
            ChunkIndexingProvider = "InMemoryShim",
            InMemoryShimEndpoint = "http://127.0.0.1:8765/"
        };

        // Act
        var result = _validator.Validate(null, config);

        // Assert
        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Fact]
    public void Validate_WithInMemoryShimProviderAndMissingEndpoint_ShouldReturnFailure() {
        // Arrange
        var config = new SearchOptions {
            IndexName = "test-index",
            BatchSize = 100,
            MaxSearchResults = 50,
            ChunkIndexingProvider = "InMemoryShim"
        };

        // Act
        var result = _validator.Validate(null, config);

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain("Search:InMemoryShimEndpoint must be a valid absolute URL when Search:ChunkIndexingProvider is InMemoryShim");
    }

    [Fact]
    public void Validate_WithInMemoryShimProviderAndNonLoopbackEndpoint_ShouldReturnFailure() {
        // Arrange
        var config = new SearchOptions {
            IndexName = "test-index",
            BatchSize = 100,
            MaxSearchResults = 50,
            ChunkIndexingProvider = "InMemoryShim",
            InMemoryShimEndpoint = "https://example.com/"
        };

        // Act
        var result = _validator.Validate(null, config);

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain("Search:InMemoryShimEndpoint must be an http or https loopback URL when Search:ChunkIndexingProvider is InMemoryShim");
    }

    [Theory]
    [InlineData("", "Search:IndexName is required")]
    [InlineData(null, "Search:IndexName is required")]
    public void Validate_WithInvalidIndexName_ShouldReturnFailure(string? indexName, string expectedError) {
        // Arrange
        var config = new SearchOptions {
            IndexName = indexName!,
            BatchSize = 100,
            MaxSearchResults = 50
        };

        // Act
        var result = _validator.Validate(null, config);

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData(0, "Search:BatchSize must be between 1 and 1000")]
    [InlineData(-1, "Search:BatchSize must be between 1 and 1000")]
    public void Validate_WithInvalidBatchSize_ShouldReturnFailure(int batchSize, string expectedError) {
        // Arrange
        var config = new SearchOptions {
            IndexName = "test-index",
            BatchSize = batchSize,
            MaxSearchResults = 50
        };

        // Act
        var result = _validator.Validate(null, config);

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData(0, "Search:MaxSearchResults must be between 1 and 100")]
    [InlineData(-1, "Search:MaxSearchResults must be between 1 and 100")]
    public void Validate_WithInvalidMaxSearchResults_ShouldReturnFailure(int maxResults, string expectedError) {
        // Arrange
        var config = new SearchOptions {
            IndexName = "test-index",
            BatchSize = 100,
            MaxSearchResults = maxResults
        };

        // Act
        var result = _validator.Validate(null, config);

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(expectedError);
    }
}
