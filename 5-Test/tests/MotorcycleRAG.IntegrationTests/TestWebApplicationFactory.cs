using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.IntegrationTests;

/// <summary>
/// Test web application factory for integration tests
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Override configuration for testing
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAI:FoundryEndpoint"] = "https://test-foundry.cognitiveservices.azure.com/",
                ["AzureAI:OpenAIEndpoint"] = "https://test-openai.openai.azure.com/",
                ["AzureAI:SearchServiceEndpoint"] = "https://test-search.search.windows.net/",
                ["AzureAI:DocumentIntelligenceEndpoint"] = "https://test-document.cognitiveservices.azure.com/",
                ["AzureAI:Models:ChatModel"] = "gpt-4o-mini",
                ["AzureAI:Models:EmbeddingModel"] = "text-embedding-3-large",
                ["AzureAI:Models:QueryPlannerModel"] = "gpt-4o",
                ["AzureAI:Models:VisionModel"] = "gpt-4-vision-preview",
                ["AzureAI:Models:MaxTokens"] = "4096",
                ["AzureAI:Models:Temperature"] = "0.1",
                ["Search:IndexName"] = "test-motorcycle-index",
                ["Search:BatchSize"] = "100",
                ["Search:MaxSearchResults"] = "50",
                ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=test-key",
                ["ApplicationInsights:EnableTelemetry"] = "false",
                ["ApplicationInsights:ApplicationName"] = "MotorcycleRAG-Test",
                ["Pipeline:MaxConcurrentExecutions"] = "3",
                ["Pipeline:DefaultTimeout"] = "00:30:00",
                ["Pipeline:MaxRetries"] = "3",
                ["Pipeline:TempDirectory"] = "temp-test",
                ["FileUpload:BaseUploadDirectory"] = "uploads-test",
                ["FileUpload:MaxFileSizeBytes"] = "52428800",
                ["FileUpload:MaxFilesPerBatch"] = "10",
                ["PipelineMonitoring:AlertsEnabled"] = "false",
                ["PipelineMonitoring:FailureRateThreshold"] = "0.10",
                ["PipelineMonitoring:MaxActiveExecutions"] = "10",
                ["ScheduledProcessing:DefaultCronExpression"] = "0 0 2 * * *",
                ["ScheduledProcessing:IsEnabledByDefault"] = "true",
                ["ScheduledProcessing:BaseDirectory"] = "data-test"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace Azure services with mocks for testing
            ReplaceWithMocks(services);
        });

        builder.UseEnvironment("Testing");
    }

    private void ReplaceWithMocks(IServiceCollection services)
    {
        // Remove existing Azure service registrations
        var servicesToRemove = services
            .Where(s => s.ServiceType.Namespace?.StartsWith("Azure") == true ||
                       s.ServiceType == typeof(IAzureOpenAIClient) ||
                       s.ServiceType == typeof(IAzureSearchClient) ||
                       s.ServiceType == typeof(IDocumentIntelligenceClient) ||
                       s.ServiceType == typeof(IMotorcycleIndexingService))
            .ToList();

        foreach (var service in servicesToRemove)
        {
            services.Remove(service);
        }

        // Add mock implementations
        var mockAzureOpenAI = new Mock<IAzureOpenAIClient>();
        mockAzureOpenAI.Setup(x => x.GenerateEmbeddingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        var mockAzureSearch = new Mock<IAzureSearchClient>();
        mockAzureSearch.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResults
            {
                Results = new List<SearchResult>(),
                TotalCount = 0
            });

        var mockDocumentIntelligence = new Mock<IDocumentIntelligenceClient>();
        mockDocumentIntelligence.Setup(x => x.AnalyzeDocumentAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentAnalysisResult
            {
                Content = "Mock extracted content",
                Pages = new List<DocumentPage>
                {
                    new DocumentPage
                    {
                        PageNumber = 1,
                        Content = "Mock page content"
                    }
                }
            });

        var mockIndexingService = new Mock<IMotorcycleIndexingService>();
        mockIndexingService.Setup(x => x.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .ReturnsAsync(new BatchIndexingResult
            {
                IsSuccessful = true,
                DocumentsProcessed = 1,
                IndexName = "test-index",
                Message = "Mock indexing successful"
            });

        services.AddSingleton(mockAzureOpenAI.Object);
        services.AddSingleton(mockAzureSearch.Object);
        services.AddSingleton(mockDocumentIntelligence.Object);
        services.AddSingleton(mockIndexingService.Object);

        // Mock resilience and correlation services
        var mockResilienceService = new Mock<IResilienceService>();
        mockResilienceService.Setup(x => x.ExecuteWithResilienceAsync(It.IsAny<Func<Task<ProcessedData>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task<ProcessedData>>, CancellationToken>((func, token) => func());

        mockResilienceService.Setup(x => x.ExecuteWithResilienceAsync(It.IsAny<Func<Task<BatchIndexingResult>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task<BatchIndexingResult>>, CancellationToken>((func, token) => func());

        var mockCorrelationService = new Mock<ICorrelationService>();
        mockCorrelationService.Setup(x => x.GetOrGenerateCorrelationId())
            .Returns("test-correlation-id");
        mockCorrelationService.Setup(x => x.StartActivity(It.IsAny<string>()))
            .Returns((IDisposable)null!);

        services.AddSingleton(mockResilienceService.Object);
        services.AddSingleton(mockCorrelationService.Object);

        // Ensure logging is configured for tests
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });
    }
}