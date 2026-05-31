using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.API.Configuration.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Azure;
using MotorcycleRAG.Persistence.DataProcessing;

namespace MotorcycleRAG.UnitTests.Presentation.API.Configuration.Services;

public class LocalDocumentIntelligenceModeTests
{
    [Fact]
    public void AddDataProcessors_WithoutDocumentIntelligenceEndpoint_ShouldRegisterDisabledPdfProcessor()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        // Act
        services.AddDataProcessors(configuration);

        // Assert
        services.Single(descriptor => descriptor.ServiceType == typeof(IDataProcessor<PDFDocument>)).ImplementationType
            .Should()
            .Be(typeof(DisabledPdfProcessor));
    }

    [Fact]
    public async Task DisabledPdfProcessor_ProcessAsync_ShouldFailPredictably()
    {
        // Arrange
        var processor = new DisabledPdfProcessor(new NullLogger<DisabledPdfProcessor>());
        var document = new PDFDocument {
            FileName = "manual.pdf",
            Content = new MemoryStream([1, 2, 3])
        };

        // Act
        var action = () => processor.ProcessAsync(document);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        exception.Message.Should().Contain("/api/ingestion/jobs/upload");
        exception.Message.Should().Contain("/api/ingestion/jobs");
        exception.Message.Should().Contain("Python processor");
    }

    [Fact]
    public async Task DisabledDocumentIntelligenceClient_AnalyzeDocumentAsync_ShouldFailPredictably()
    {
        // Arrange
        var client = new DisabledDocumentIntelligenceClient(new NullLogger<DisabledDocumentIntelligenceClient>());
        await using var stream = new MemoryStream([1, 2, 3]);

        // Act
        var action = () => client.AnalyzeDocumentAsync(stream, "application/pdf");

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        exception.Message.Should().Contain("/api/ingestion/jobs/upload");
        exception.Message.Should().Contain("Python processor");
    }

    [Fact]
    public void AddHealthChecks_WithoutDocumentIntelligenceEndpoint_ShouldSkipDocumentIntelligenceRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        // Act
        services.AddHealthChecks().AddHealthChecks(configuration);
        using var serviceProvider = services.BuildServiceProvider();
        var registrations = serviceProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        // Assert
        registrations.Select(registration => registration.Name)
            .Should()
            .NotContain("azure_document_intelligence");
    }

    [Fact]
    public void AzureAIConfigurationValidator_WithoutDocumentIntelligenceEndpoint_ShouldReturnSuccess()
    {
        // Arrange
        var validator = new AzureAIConfigurationValidator();
        var options = new AzureFoundryOptions {
            SearchServiceEndpoint = "https://test-search.search.windows.net/",
            Models = new ModelOptions {
                ChatModel = "gpt-4o-mini",
                EmbeddingModel = "text-embedding-3-large",
                MaxTokens = 4096,
                Temperature = 0.1f
            },
            OrchestratorAgentName = "orchestrator",
            VectorSearchAgentName = "vector",
            WebSearchAgentName = "web",
            PDFSearchAgentName = "pdf",
            GraphQueryAgentName = "graph"
        };

        // Act
        var result = ((IValidateOptions<AzureFoundryOptions>)validator).Validate(null, options);

        // Assert
        result.Should().Be(ValidateOptionsResult.Success);
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["AzureAI:SearchServiceEndpoint"] = "https://test-search.search.windows.net/",
                ["Search:IndexName"] = "test-index",
                ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=test-key"
            })
            .Build();
}
