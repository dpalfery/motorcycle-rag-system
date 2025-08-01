using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using MotorcycleRAG.Infrastructure.Azure;

namespace MotorcycleRAG.UnitTests.Azure;

public class ServiceCollectionExtensionsTests
{
    private readonly IConfiguration _configuration;
    private readonly ServiceCollection _services;

    public ServiceCollectionExtensionsTests()
    {
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAI:FoundryEndpoint"] = "https://test-foundry.cognitiveservices.azure.com/",
            ["AzureAI:OpenAIEndpoint"] = "https://test-openai.openai.azure.com/",
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
    }

    [Fact]
    public void AddAzureServices_WithValidConfiguration_ShouldRegisterAllServices()
    {
        // Act
        _services.AddAzureServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        serviceProvider.GetService<IAzureOpenAIClient>().Should().NotBeNull();
        serviceProvider.GetService<IAzureSearchClient>().Should().NotBeNull();
        serviceProvider.GetService<IDocumentIntelligenceClient>().Should().NotBeNull();
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterConfigurationOptions()
    {
        // Act
        _services.AddAzureServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        var azureConfig = serviceProvider.GetService<IOptions<AzureAIConfiguration>>();
        azureConfig.Should().NotBeNull();
        azureConfig!.Value.OpenAIEndpoint.Should().Be("https://test-openai.openai.azure.com/");

        var searchConfig = serviceProvider.GetService<IOptions<SearchConfiguration>>();
        searchConfig.Should().NotBeNull();
        searchConfig!.Value.IndexName.Should().Be("test-index");

        var telemetryConfig = serviceProvider.GetService<IOptions<TelemetryConfiguration>>();
        telemetryConfig.Should().NotBeNull();
        telemetryConfig!.Value.ApplicationName.Should().Be("MotorcycleRAG");
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterServicesAsSingletons()
    {
        // Act
        _services.AddAzureServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        var client1 = serviceProvider.GetService<IAzureOpenAIClient>();
        var client2 = serviceProvider.GetService<IAzureOpenAIClient>();
        
        client1.Should().BeSameAs(client2);
    }

    [Fact]
    public void AddAzureServices_ShouldRegisterHttpClient()
    {
        // Act
        _services.AddAzureServices(_configuration);
        var serviceProvider = _services.BuildServiceProvider();

        // Assert
        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
        httpClientFactory.Should().NotBeNull();
    }
}

public class AzureAIConfigurationValidatorTests
{
    private readonly AzureAIConfigurationValidator _validator;

    public AzureAIConfigurationValidatorTests()
    {
        _validator = new AzureAIConfigurationValidator();
    }

    [Fact]
    public void Validate_WithValidConfiguration_ShouldReturnSuccess()
    {
        // Arrange
        var config = new AzureAIConfiguration
        {
            FoundryEndpoint = "https://test-foundry.cognitiveservices.azure.com/",
            OpenAIEndpoint = "https://test-openai.openai.azure.com/",
            SearchServiceEndpoint = "https://test-search.search.windows.net/",
            DocumentIntelligenceEndpoint = "https://test-document-intelligence.cognitiveservices.azure.com/",
            Models = new ModelConfiguration
            {
                ChatModel = "gpt-4o-mini",
                EmbeddingModel = "text-embedding-3-large",
                QueryPlannerModel = "gpt-4o",
                VisionModel = "gpt-4-vision-preview",
                MaxTokens = 4096,
                Temperature = 0.1f,
                TopP = 1.0f
            },
            Retry = new RetryConfiguration
            {
                MaxRetries = 3,
                BaseDelaySeconds = 2,
                MaxDelaySeconds = 60,
                UseExponentialBackoff = true
            }
        };

        // Act
        var result = _validator.Validate(null, config);

        // Assert
        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Theory]
    [InlineData("", "AzureAI:FoundryEndpoint is required")]
    [InlineData("invalid-uri", "AzureAI:FoundryEndpoint must be a valid URI")]
    public void Validate_WithInvalidFoundryEndpoint_ShouldReturnFailure(string endpoint, string expectedError)
    {
        // Arrange
        var config = CreateValidConfiguration();
        config.FoundryEndpoint = endpoint;

        // Act
        var result = _validator.Validate(null, config);

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData("", "AzureAI:OpenAIEndpoint is required")]
    [InlineData("invalid-uri", "AzureAI:OpenAIEndpoint must be a valid URI")]
    public void Validate_WithInvalidOpenAIEndpoint_ShouldReturnFailure(string endpoint, string expectedError)
    {
        // Arrange
        var config = CreateValidConfiguration();
        config.OpenAIEndpoint = endpoint;

        // Act
        var result = _validator.Validate(null, config);

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData(0, "AzureAI:Models:MaxTokens must be greater than 0")]
    [InlineData(-1, "AzureAI:Models:MaxTokens must be greater than 0")]
    public void Validate_WithInvalidMaxTokens_ShouldReturnFailure(int maxTokens, string expectedError)
    {
        // Arrange
        var config = CreateValidConfiguration();
        config.Models.MaxTokens = maxTokens;

        // Act
        var result = _validator.Validate(null, config);

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData(-0.1f, "AzureAI:Models:Temperature must be between 0 and 2")]
    [InlineData(2.1f, "AzureAI:Models:Temperature must be between 0 and 2")]
    public void Validate_WithInvalidTemperature_ShouldReturnFailure(float temperature, string expectedError)
    {
        // Arrange
        var config = CreateValidConfiguration();
        config.Models.Temperature = temperature;

        // Act
        var result = _validator.Validate(null, config);

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData(0, "AzureAI:Retry:MaxRetries must be greater than 0")]
    [InlineData(-1, "AzureAI:Retry:MaxRetries must be greater than 0")]
    public void Validate_WithInvalidMaxRetries_ShouldReturnFailure(int maxRetries, string expectedError)
    {
        // Arrange
        var config = CreateValidConfiguration();
        config.Retry.MaxRetries = maxRetries;

        // Act
        var result = _validator.Validate(null, config);

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(expectedError);
    }

    private static AzureAIConfiguration CreateValidConfiguration()
    {
        return new AzureAIConfiguration
        {
            FoundryEndpoint = "https://test-foundry.cognitiveservices.azure.com/",
            OpenAIEndpoint = "https://test-openai.openai.azure.com/",
            SearchServiceEndpoint = "https://test-search.search.windows.net/",
            DocumentIntelligenceEndpoint = "https://test-document-intelligence.cognitiveservices.azure.com/",
            Models = new ModelConfiguration
            {
                ChatModel = "gpt-4o-mini",
                EmbeddingModel = "text-embedding-3-large",
                QueryPlannerModel = "gpt-4o",
                VisionModel = "gpt-4-vision-preview",
                MaxTokens = 4096,
                Temperature = 0.1f,
                TopP = 1.0f
            },
            Retry = new RetryConfiguration
            {
                MaxRetries = 3,
                BaseDelaySeconds = 2,
                MaxDelaySeconds = 60,
                UseExponentialBackoff = true
            }
        };
    }
}

public class SearchConfigurationValidatorTests
{
    private readonly SearchConfigurationValidator _validator;

    public SearchConfigurationValidatorTests()
    {
        _validator = new SearchConfigurationValidator();
    }

    [Fact]
    public void Validate_WithValidConfiguration_ShouldReturnSuccess()
    {
        // Arrange
        var config = new SearchConfiguration
        {
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

    [Theory]
    [InlineData("", "Search:IndexName is required")]
    [InlineData(null, "Search:IndexName is required")]
    public void Validate_WithInvalidIndexName_ShouldReturnFailure(string? indexName, string expectedError)
    {
        // Arrange
        var config = new SearchConfiguration
        {
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
    [InlineData(0, "Search:BatchSize must be greater than 0")]
    [InlineData(-1, "Search:BatchSize must be greater than 0")]
    public void Validate_WithInvalidBatchSize_ShouldReturnFailure(int batchSize, string expectedError)
    {
        // Arrange
        var config = new SearchConfiguration
        {
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
    [InlineData(0, "Search:MaxSearchResults must be greater than 0")]
    [InlineData(-1, "Search:MaxSearchResults must be greater than 0")]
    public void Validate_WithInvalidMaxSearchResults_ShouldReturnFailure(int maxResults, string expectedError)
    {
        // Arrange
        var config = new SearchConfiguration
        {
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