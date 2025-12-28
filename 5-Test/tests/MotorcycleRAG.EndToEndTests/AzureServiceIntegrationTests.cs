using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Persistence.Azure;
using MotorcycleRAG.Domain.Models;
using MotorcycleRAG.Contracts.Options;

namespace MotorcycleRAG.EndToEndTests;

/// <summary>
/// Integration tests that verify connectivity and functionality with real Azure services.
/// These tests require actual Azure service endpoints and credentials.
/// </summary>
public class AzureServiceIntegrationTests : IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AzureServiceIntegrationTests> _logger;

    public AzureServiceIntegrationTests()
    {
        _configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.EndToEnd.json")
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton(_configuration);
        
        // Add Azure service clients
        services.AddSingleton<DefaultAzureCredential>();
        services.AddSingleton<AzureOpenAIClientWrapper>();
        services.AddSingleton<AzureSearchClientWrapper>();
        services.AddSingleton<DocumentIntelligenceClientWrapper>();
        
        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<AzureServiceIntegrationTests>>();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AzureOpenAI_Connection_SuccessfullyConnectsAndGeneratesEmbeddings()
    {
        // Skip if not configured for real Azure services
        if (!_configuration.GetValue<bool>("TestConfiguration:UseRealAzureServices"))
        {
            return;
        }

        // Arrange
        var openAIClient = _serviceProvider.GetRequiredService<AzureOpenAIClientWrapper>();
        var testText = "Honda CBR600RR is a 600cc supersport motorcycle with inline-4 engine.";

        // Act
        var embeddings = await openAIClient.GenerateEmbeddingsAsync(testText);

        // Assert
        embeddings.Should().NotBeNull();
        embeddings.Should().HaveCount(1);
        embeddings[0].Should().HaveCountGreaterThan(1000); // text-embedding-3-large has 3072 dimensions
        
        _logger.LogInformation("Successfully generated embeddings with {Dimensions} dimensions", embeddings[0].Length);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AzureOpenAI_ChatCompletion_GeneratesRelevantResponse()
    {
        // Skip if not configured for real Azure services
        if (!_configuration.GetValue<bool>("TestConfiguration:UseRealAzureServices"))
        {
            return;
        }

        // Arrange
        var openAIClient = _serviceProvider.GetRequiredService<AzureOpenAIClientWrapper>();
        var query = "What are the key specifications of Honda CBR600RR?";
        var context = "Honda CBR600RR: 599cc inline-4 engine, 118hp, 194kg dry weight, 260km/h top speed.";

        // Act
        var response = await openAIClient.GenerateChatCompletionAsync(query, context);

        // Assert
        response.Should().NotBeNullOrEmpty();
        response.Should().ContainAny("Honda", "CBR600RR", "599", "118");
        
        _logger.LogInformation("Generated chat response: {Response}", response[..Math.Min(100, response.Length)]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AzureAISearch_IndexOperations_SuccessfullyCreatesAndSearchesIndex()
    {
        // Skip if not configured for real Azure services
        if (!_configuration.GetValue<bool>("TestConfiguration:UseRealAzureServices"))
        {
            return;
        }

        // Arrange
        var searchClient = _serviceProvider.GetRequiredService<AzureSearchClientWrapper>();
        var testIndexName = $"test-motorcycle-index-{Guid.NewGuid():N}";
        
        try
        {
            // Act - Create test index
            await searchClient.CreateIndexAsync(testIndexName);
            
            // Add test document
            var testDocument = new MotorcycleDocument
            {
                Id = "test-doc-001",
                Title = "Honda CBR600RR Test Document",
                Content = "Honda CBR600RR specifications and performance data for testing purposes.",
                Type = DocumentType.Specification,
                Metadata = new DocumentMetadata
                {
                    Source = "integration-test",
                    ProcessedAt = DateTime.UtcNow
                }
            };

            await searchClient.IndexDocumentAsync(testIndexName, testDocument);
            
            // Wait for indexing to complete
            await Task.Delay(TimeSpan.FromSeconds(5));
            
            // Search for the document
            var searchResults = await searchClient.SearchAsync(testIndexName, "Honda CBR600RR", new SearchOptions
            {
                MaxResults = 10,
                IncludeSemanticSearch = true
            });

            // Assert
            searchResults.Should().NotBeEmpty();
            searchResults.Should().Contain(r => r.Id == "test-doc-001");
            
            _logger.LogInformation("Successfully indexed and searched {DocumentCount} documents", searchResults.Count);
        }
        finally
        {
            // Cleanup - Delete test index
            try
            {
                await searchClient.DeleteIndexAsync(testIndexName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup test index {IndexName}", testIndexName);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DocumentIntelligence_PDFProcessing_ExtractsTextFromPDF()
    {
        // Skip if not configured for real Azure services
        if (!_configuration.GetValue<bool>("TestConfiguration:UseRealAzureServices"))
        {
            return;
        }

        // Arrange
        var documentClient = _serviceProvider.GetRequiredService<DocumentIntelligenceClientWrapper>();
        var testPdfPath = Path.Combine(_configuration["TestConfiguration:TestDataPath"]!, "sample-maintenance-manual.pdf");
        
        // Create a simple test PDF if it doesn't exist
        if (!File.Exists(testPdfPath))
        {
            await CreateTestPDFAsync(testPdfPath);
        }

        // Act
        var extractedContent = await documentClient.ExtractTextFromPDFAsync(testPdfPath);

        // Assert
        extractedContent.Should().NotBeNull();
        extractedContent.Content.Should().NotBeNullOrEmpty();
        extractedContent.Pages.Should().NotBeEmpty();
        
        _logger.LogInformation("Successfully extracted {CharacterCount} characters from PDF", extractedContent.Content.Length);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AzureServices_ResiliencePatterns_HandlesTransientFailures()
    {
        // Skip if not configured for real Azure services
        if (!_configuration.GetValue<bool>("TestConfiguration:UseRealAzureServices"))
        {
            return;
        }

        // Arrange
        var openAIClient = _serviceProvider.GetRequiredService<AzureOpenAIClientWrapper>();
        var tasks = new List<Task<string>>();

        // Act - Generate multiple concurrent requests to test rate limiting and retry logic
        for (int i = 0; i < 10; i++)
        {
            var task = openAIClient.GenerateChatCompletionAsync(
                $"Test query {i} for resilience testing",
                "Test context for resilience patterns"
            );
            tasks.Add(task);
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(10);
        results.Should().AllSatisfy(result => result.Should().NotBeNullOrEmpty());
        
        _logger.LogInformation("Successfully completed {RequestCount} concurrent requests", results.Length);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AzureServices_Authentication_ValidatesCredentials()
    {
        // Skip if not configured for real Azure services
        if (!_configuration.GetValue<bool>("TestConfiguration:UseRealAzureServices"))
        {
            return;
        }

        // Arrange
        var credential = _serviceProvider.GetRequiredService<DefaultAzureCredential>();

        // Act & Assert - This will throw if authentication fails
        var token = await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }));
        
        token.Token.Should().NotBeNullOrEmpty();
        token.ExpiresOn.Should().BeAfter(DateTimeOffset.UtcNow);
        
        _logger.LogInformation("Successfully authenticated with Azure services");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EndToEndDataPipeline_CSVProcessing_CompletesSuccessfully()
    {
        // Skip if not configured for real Azure services
        if (!_configuration.GetValue<bool>("TestConfiguration:UseRealAzureServices"))
        {
            return;
        }

        // Arrange
        var csvPath = Path.Combine(_configuration["TestConfiguration:TestDataPath"]!, "sample-motorcycle-specs.csv");
        var processor = _serviceProvider.GetRequiredService<MotorcycleCSVProcessor>();
        var searchClient = _serviceProvider.GetRequiredService<AzureSearchClientWrapper>();

        // Act
        var csvData = await File.ReadAllBytesAsync(csvPath);
        var processingResult = await processor.ProcessAsync(new CSVFile
        {
            FileName = "sample-motorcycle-specs.csv",
            Content = csvData,
            ContentType = "text/csv"
        });

        // Assert
        processingResult.Should().NotBeNull();
        processingResult.Success.Should().BeTrue();
        processingResult.ProcessedDocuments.Should().NotBeEmpty();
        
        _logger.LogInformation("Successfully processed CSV with {DocumentCount} documents", processingResult.ProcessedDocuments.Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EndToEndDataPipeline_PDFProcessing_CompletesSuccessfully()
    {
        // Skip if not configured for real Azure services
        if (!_configuration.GetValue<bool>("TestConfiguration:UseRealAzureServices"))
        {
            return;
        }

        // Arrange
        var pdfPath = Path.Combine(_configuration["TestConfiguration:TestDataPath"]!, "sample-maintenance-manual.pdf");
        var processor = _serviceProvider.GetRequiredService<MotorcyclePDFProcessor>();

        // Create test PDF if it doesn't exist
        if (!File.Exists(pdfPath))
        {
            await CreateTestPDFAsync(pdfPath);
        }

        // Act
        var pdfData = await File.ReadAllBytesAsync(pdfPath);
        var processingResult = await processor.ProcessAsync(new PDFDocument
        {
            FileName = "sample-maintenance-manual.pdf",
            Content = pdfData,
            ContentType = "application/pdf"
        });

        // Assert
        processingResult.Should().NotBeNull();
        processingResult.Success.Should().BeTrue();
        processingResult.ProcessedDocuments.Should().NotBeEmpty();
        
        _logger.LogInformation("Successfully processed PDF with {DocumentCount} documents", processingResult.ProcessedDocuments.Count);
    }

    private async Task CreateTestPDFAsync(string filePath)
    {
        // Create a simple test PDF content (this would normally use a PDF library)
        var testContent = """
            MOTORCYCLE MAINTENANCE MANUAL
            
            Honda CBR600RR Maintenance Schedule
            
            Engine Oil Change:
            1. Warm up the engine to operating temperature
            2. Remove the drain plug and drain oil completely
            3. Replace the oil filter
            4. Install new drain plug with new gasket
            5. Add new engine oil (3.7L capacity)
            6. Check oil level and top up if necessary
            
            Brake System Maintenance:
            1. Check brake fluid level monthly
            2. Replace brake fluid every 2 years
            3. Inspect brake pads for wear
            4. Check brake disc thickness
            
            Chain Maintenance:
            1. Clean chain every 500km
            2. Lubricate chain after cleaning
            3. Check chain tension regularly
            4. Replace chain and sprockets as needed
            """;

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, testContent);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
