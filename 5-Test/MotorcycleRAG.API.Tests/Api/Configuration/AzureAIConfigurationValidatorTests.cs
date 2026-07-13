using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.API.Tests.Api.Configuration;

public class AzureAIConfigurationValidatorTests
{
    private readonly IValidateOptions<AzureFoundryOptions> _validator = new AzureAIConfigurationValidator();

    private static AzureFoundryOptions Valid() => new()
    {
        FoundryEndpoint = "https://foundry.example.com",
        SearchServiceEndpoint = "https://search.example.com",
        DocumentIntelligenceEndpoint = "https://di.example.com",
        OrchestratorAgentName = "orch",
        VectorSearchAgentName = "vec",
        WebSearchAgentName = "web",
        PDFSearchAgentName = "pdf",
        GraphQueryAgentName = "graph",
        Models = new ModelOptions { ChatModel = "chat", EmbeddingModel = "embed", MaxTokens = 1000, Temperature = 0.5f }
    };

    [Fact]
    public void Validate_ValidOptions_Succeeds()
        => _validator.Validate(null, Valid()).Succeeded.Should().BeTrue();

    [Fact]
    public void Validate_InvalidFoundryUrl_Fails()
    {
        var opts = Valid(); opts.FoundryEndpoint = "not-a-url";
        _validator.Validate(null, opts).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingSearchEndpoint_Fails()
    {
        var opts = Valid(); opts.SearchServiceEndpoint = "";
        _validator.Validate(null, opts).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_InvalidSearchUrl_Fails()
    {
        var opts = Valid(); opts.SearchServiceEndpoint = "not-a-url";
        _validator.Validate(null, opts).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_InvalidDocumentIntelligenceUrl_Fails()
    {
        var opts = Valid(); opts.DocumentIntelligenceEndpoint = "not-a-url";
        _validator.Validate(null, opts).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_NullModels_Fails()
    {
        var opts = Valid(); opts.Models = null!;
        _validator.Validate(null, opts).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingChatModel_Fails()
    {
        var opts = Valid(); opts.Models.ChatModel = "";
        _validator.Validate(null, opts).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingEmbeddingModel_Fails()
    {
        var opts = Valid(); opts.Models.EmbeddingModel = "";
        _validator.Validate(null, opts).Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_MaxTokensOutOfRange_Fails(int maxTokens)
    {
        var opts = Valid(); opts.Models.MaxTokens = maxTokens;
        _validator.Validate(null, opts).Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(2.1f)]
    public void Validate_TemperatureOutOfRange_Fails(float temp)
    {
        var opts = Valid(); opts.Models.Temperature = temp;
        _validator.Validate(null, opts).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingAgentNames_Fails()
    {
        var opts = Valid();
        opts.OrchestratorAgentName = "";
        _validator.Validate(null, opts).Failed.Should().BeTrue();

        opts.OrchestratorAgentName = "orch";
        opts.VectorSearchAgentName = "";
        _validator.Validate(null, opts).Failed.Should().BeTrue();

        opts.VectorSearchAgentName = "vec";
        opts.WebSearchAgentName = "";
        _validator.Validate(null, opts).Failed.Should().BeTrue();

        opts.WebSearchAgentName = "web";
        opts.PDFSearchAgentName = "";
        _validator.Validate(null, opts).Failed.Should().BeTrue();

        opts.PDFSearchAgentName = "pdf";
        opts.GraphQueryAgentName = "";
        _validator.Validate(null, opts).Failed.Should().BeTrue();
    }
}
