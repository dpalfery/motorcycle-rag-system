using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Presentation.API.Configuration;

public sealed class ConfigurationValidatorsCoverageTests
{
    [Fact]
    public void AzureAiValidator_InvalidOptionalEndpointsAndMissingSettings_ReportsEveryFailure()
    {
        var options = new AzureFoundryOptions
        {
            FoundryEndpoint = "not-a-url",
            SearchServiceEndpoint = "also-not-a-url",
            DocumentIntelligenceEndpoint = "invalid",
            Models = new ModelOptions { ChatModel = " ", EmbeddingModel = " ", MaxTokens = 0, Temperature = 3f }
        };

        var result = ((IValidateOptions<AzureFoundryOptions>)new AzureAIConfigurationValidator())
            .Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain([
            "AzureAI:FoundryEndpoint must be a valid URL if provided",
            "AzureAI:SearchServiceEndpoint must be a valid URL",
            "AzureAI:DocumentIntelligenceEndpoint must be a valid URL",
            "AzureAI:Models:ChatModel is required",
            "AzureAI:Models:EmbeddingModel is required",
            "AzureAI:Models:MaxTokens must be greater than 0",
            "AzureAI:Models:Temperature must be between 0 and 2",
            "AzureAI:OrchestratorAgentName is required (written by deploy pipeline to Key Vault)",
            "AzureAI:VectorSearchAgentName is required (written by deploy pipeline to Key Vault)",
            "AzureAI:WebSearchAgentName is required (written by deploy pipeline to Key Vault)",
            "AzureAI:PDFSearchAgentName is required (written by deploy pipeline to Key Vault)",
            "AzureAI:GraphQueryAgentName is required (written by deploy pipeline to Key Vault)"]);
    }

    [Fact]
    public void AzureAiValidator_MissingSearchAndModels_ReportsRequiredSettings()
    {
        var result = ((IValidateOptions<AzureFoundryOptions>)new AzureAIConfigurationValidator())
            .Validate(null, new AzureFoundryOptions { Models = null! });

        result.Failures.Should().Contain([
            "AzureAI:SearchServiceEndpoint is required",
            "AzureAI:Models configuration is required"]);
    }

    [Theory]
    [InlineData(0, 10, "Search:BatchIndexTimeoutSeconds must be between 1 and 600")]
    [InlineData(601, 10, "Search:BatchIndexTimeoutSeconds must be between 1 and 600")]
    [InlineData(60, 101, "Search:MaxSearchResults must be between 1 and 100")]
    public void SearchValidator_InvalidUpperBounds_ReportsFailure(
        int batchTimeoutSeconds,
        int maxSearchResults,
        string expectedFailure)
    {
        var result = ValidateSearch(new SearchOptions
        {
            IndexName = "manuals",
            BatchSize = 10,
            BatchIndexTimeoutSeconds = batchTimeoutSeconds,
            MaxSearchResults = maxSearchResults
        });

        result.Failures.Should().Contain(expectedFailure);
    }

    [Theory]
    [InlineData("ftp://localhost:8080")]
    [InlineData("http://not-loopback.example:8080")]
    [InlineData("https://192.0.2.1:8080")]
    public void SearchValidator_InMemoryShimWithNonLoopbackOrUnsupportedScheme_ReportsFailure(string endpoint)
    {
        var result = ValidateSearch(new SearchOptions
        {
            IndexName = "manuals",
            BatchSize = 10,
            BatchIndexTimeoutSeconds = 60,
            MaxSearchResults = 10,
            ChunkIndexingProvider = "InMemoryShim",
            InMemoryShimEndpoint = endpoint
        });

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle()
            .Which.Should().Contain("http or https loopback URL");
    }

    [Fact]
    public void SearchValidator_InMemoryShimWithMalformedEndpoint_ReportsUrlFailure()
    {
        var result = ValidateSearch(new SearchOptions
        {
            IndexName = "manuals",
            BatchSize = 10,
            BatchIndexTimeoutSeconds = 60,
            MaxSearchResults = 10,
            ChunkIndexingProvider = "InMemoryShim",
            InMemoryShimEndpoint = "not a valid URI"
        });

        result.Failures.Should().ContainSingle()
            .Which.Should().Contain("valid absolute URL");
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("https://127.0.0.1:8080")]
    [InlineData("https://[::1]:8080")]
    public void SearchValidator_InMemoryShimWithLoopbackHttpEndpoint_Succeeds(string endpoint)
    {
        var result = ValidateSearch(new SearchOptions
        {
            IndexName = "manuals",
            BatchSize = 10,
            BatchIndexTimeoutSeconds = 60,
            MaxSearchResults = 10,
            ChunkIndexingProvider = "InMemoryShim",
            InMemoryShimEndpoint = endpoint
        });

        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Fact]
    public void HealthCheckTags_ExposeExpectedImmutableTagSets()
    {
        HealthCheckTags.AzureSearch.Should().Equal("azure", "search");
        HealthCheckTags.AzureOpenAI.Should().Equal("azure", "ai");
        HealthCheckTags.AzureDocument.Should().Equal("azure", "document");
        HealthCheckTags.AzureFoundry.Should().Equal("azure", "foundry");
        HealthCheckTags.SqlDatabase.Should().Equal("database", "sql");
        HealthCheckTags.Pipeline.Should().Equal("pipeline", "processing");
        HealthCheckTags.System.Should().Equal("system");
    }

    private static ValidateOptionsResult ValidateSearch(SearchOptions options) =>
        ((IValidateOptions<SearchOptions>)new SearchConfigurationValidator()).Validate(null, options);
}
