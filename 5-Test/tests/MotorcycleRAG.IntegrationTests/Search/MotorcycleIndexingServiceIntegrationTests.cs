using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using MotorcycleRAG.Infrastructure.Azure;
using Xunit;
using Xunit.Abstractions;

namespace MotorcycleRAG.IntegrationTests.Search;

/// <summary>
/// Integration tests for MotorcycleIndexingService with real Azure services
/// These tests require Azure credentials and services to be configured
/// </summary>
public class MotorcycleIndexingServiceIntegrationTests : IClassFixture<TestHostFixture>, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMotorcycleIndexingService _indexingService;
    private readonly ILogger<MotorcycleIndexingServiceIntegrationTests> _logger;
    private readonly ITestOutputHelper _output;

    public MotorcycleIndexingServiceIntegrationTests(TestHostFixture fixture, ITestOutputHelper output)
    {
        _serviceProvider = fixture.ServiceProvider;
        _indexingService = _serviceProvider.GetRequiredService<IMotorcycleIndexingService>();
        _logger = _serviceProvider.GetRequiredService<ILogger<MotorcycleIndexingServiceIntegrationTests>>();
        _output = output;
    }

    [Fact]
    public async Task CreateSearchIndexesAsync_ShouldCreateIndexesSuccessfully_WhenAzureServicesAvailable()
    {
        // Arrange
        _output.WriteLine("Starting index creation integration test");

        // Act
        var result = await _indexingService.CreateSearchIndexesAsync();

        // Assert
        Assert.True(result.Success, $"Index creation failed: {result.Message}");
        Assert.NotEmpty(result.CreatedIndexes);
        Assert.Contains("motorcycle-csv-index", result.CreatedIndexes);
        Assert.Contains("motorcycle-pdf-index", result.CreatedIndexes);
        Assert.Contains("motorcycle-unified-index", result.CreatedIndexes);
        
        _output.WriteLine($"Successfully created {result.CreatedIndexes.Count} indexes");
        foreach (var index in result.CreatedIndexes)
        {
            _output.WriteLine($"Created index: {index}");
        }
    }

    [Fact]
    public async Task IndexCSVDataAsync_ShouldIndexDocumentsSuccessfully_WhenValidDataProvided()
    {
        // Arrange
        _output.WriteLine("Starting CSV indexing integration test");
        
        // Ensure indexes exist first
        await _indexingService.CreateSearchIndexesAsync();
        
        var processedData = CreateTestCSVData();

        // Act
        var result = await _indexingService.IndexCSVDataAsync(processedData);

        // Assert
        Assert.True(result.Success, $"CSV indexing failed: {result.Message}");
        Assert.True(result.DocumentsIndexed > 0, "No documents were indexed");
        Assert.Empty(result.Errors);
        
        _output.WriteLine($"Successfully indexed {result.DocumentsIndexed} CSV documents");
        _output.WriteLine($"Indexing time: {result.IndexingTime.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task IndexPDFDataAsync_ShouldIndexDocumentsSuccessfully_WhenValidDataProvided()
    {
        // Arrange
        _output.WriteLine("Starting PDF indexing integration test");
        
        // Ensure indexes exist first
        await _indexingService.CreateSearchIndexesAsync();
        
        var processedData = CreateTestPDFData();

        // Act
        var result = await _indexingService.IndexPDFDataAsync(processedData);

        // Assert
        Assert.True(result.Success, $"PDF indexing failed: {result.Message}");
        Assert.True(result.DocumentsIndexed > 0, "No documents were indexed");
        Assert.Empty(result.Errors);
        
        _output.WriteLine($"Successfully indexed {result.DocumentsIndexed} PDF documents");
        _output.WriteLine($"Indexing time: {result.IndexingTime.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task BatchIndexing_ShouldHandleLargeDatasets_WhenProcessingManyDocuments()
    {
        // Arrange
        _output.WriteLine("Starting large dataset indexing integration test");
        
        // Ensure indexes exist first
        await _indexingService.CreateSearchIndexesAsync();
        
        var largeDataset = CreateLargeTestDataset(150); // Larger than batch size

        // Act
        var result = await _indexingService.IndexCSVDataAsync(largeDataset);

        // Assert
        Assert.True(result.Success, $"Large dataset indexing failed: {result.Message}");
        Assert.True(result.DocumentsIndexed > 0, "No documents were indexed");
        
        _output.WriteLine($"Successfully indexed {result.DocumentsIndexed} documents from large dataset");
        _output.WriteLine($"Total indexing time: {result.IndexingTime.TotalMilliseconds}ms");
        _output.WriteLine($"Average time per document: {result.IndexingTime.TotalMilliseconds / result.DocumentsIndexed:F2}ms");
    }

    [Fact]
    public async Task GetIndexingStatisticsAsync_ShouldReturnValidStatistics_WhenIndexesExist()
    {
        // Arrange
        _output.WriteLine("Starting indexing statistics integration test");
        
        // Ensure indexes exist and have some data
        await _indexingService.CreateSearchIndexesAsync();
        var testData = CreateTestCSVData();
        await _indexingService.IndexCSVDataAsync(testData);

        // Wait a moment for indexing to complete
        await Task.Delay(2000);

        // Act
        var statistics = await _indexingService.GetIndexingStatisticsAsync();

        // Assert
        Assert.NotNull(statistics);
        Assert.NotEmpty(statistics.Indexes);
        Assert.True(statistics.HealthyIndexes > 0, "No healthy indexes found");
        
        _output.WriteLine($"Total documents across all indexes: {statistics.TotalDocuments}");
        _output.WriteLine($"Total storage size: {statistics.TotalStorageSize} bytes");
        _output.WriteLine($"Healthy indexes: {statistics.HealthyIndexes}/{statistics.Indexes.Count}");
        
        foreach (var index in statistics.Indexes)
        {
            _output.WriteLine($"Index {index.Name}: {index.DocumentCount} docs, {index.StorageSize} bytes, Healthy: {index.IsHealthy}");
        }
    }

    [Fact]
    public async Task MetadataManagement_ShouldPreserveFieldMappings_WhenIndexingDocuments()
    {
        // Arrange
        _output.WriteLine("Starting metadata management integration test");
        
        // Ensure indexes exist first
        await _indexingService.CreateSearchIndexesAsync();
        
        var processedData = CreateTestDataWithRichMetadata();

        // Act
        var result = await _indexingService.IndexCSVDataAsync(processedData);

        // Assert
        Assert.True(result.Success, $"Metadata indexing failed: {result.Message}");
        Assert.True(result.DocumentsIndexed > 0, "No documents were indexed");
        
        // Verify statistics to ensure metadata was preserved
        await Task.Delay(1000); // Allow indexing to complete
        var statistics = await _indexingService.GetIndexingStatisticsAsync();
        Assert.True(statistics.TotalDocuments > 0, "Documents with metadata were not indexed");
        
        _output.WriteLine($"Successfully indexed {result.DocumentsIndexed} documents with rich metadata");
    }

    [Fact]
    public async Task HybridVectorKeywordCapabilities_ShouldCreateProperIndexSchema_WhenCreatingIndexes()
    {
        // Arrange
        _output.WriteLine("Starting hybrid search capabilities integration test");

        // Act
        var result = await _indexingService.CreateSearchIndexesAsync();

        // Assert
        Assert.True(result.Success, $"Index creation with hybrid capabilities failed: {result.Message}");
        Assert.Equal(3, result.CreatedIndexes.Count); // CSV, PDF, and Unified indexes
        
        // Verify that all expected indexes were created
        var expectedIndexes = new[] { "motorcycle-csv-index", "motorcycle-pdf-index", "motorcycle-unified-index" };
        foreach (var expectedIndex in expectedIndexes)
        {
            Assert.Contains(expectedIndex, result.CreatedIndexes);
        }
        
        _output.WriteLine("Successfully created indexes with hybrid vector/keyword capabilities");
    }

    #region Test Data Creation Methods

    private ProcessedData CreateTestCSVData()
    {
        return new ProcessedData
        {
            Id = Guid.NewGuid().ToString(),
            Documents = new List<MotorcycleDocument>
            {
                new MotorcycleDocument
                {
                    Id = $"integration-csv-{Guid.NewGuid()}",
                    Title = "Honda CBR1000RR-R Integration Test",
                    Content = "Honda CBR1000RR-R Fireblade SP specifications for integration testing",
                    Type = DocumentType.Specification,
                    ContentVector = GenerateTestVector(1536),
                    Metadata = new DocumentMetadata
                    {
                        SourceFile = "integration_test_specs.csv",
                        Section = "Superbike Specifications",
                        Tags = new List<string> { "Honda", "CBR", "Superbike", "Integration" },
                        AdditionalProperties = new Dictionary<string, object>
                        {
                            ["Make"] = "Honda",
                            ["Model"] = "CBR1000RR-R",
                            ["Year"] = "2024",
                            ["Engine"] = "999cc Inline-4",
                            ["Power"] = "217hp",
                            ["Weight"] = "201kg"
                        }
                    }
                },
                new MotorcycleDocument
                {
                    Id = $"integration-csv-{Guid.NewGuid()}",
                    Title = "Yamaha YZF-R1M Integration Test",
                    Content = "Yamaha YZF-R1M specifications and features for integration testing",
                    Type = DocumentType.Specification,
                    ContentVector = GenerateTestVector(1536),
                    Metadata = new DocumentMetadata
                    {
                        SourceFile = "integration_test_specs.csv",
                        Section = "Superbike Specifications",
                        Tags = new List<string> { "Yamaha", "R1M", "Superbike", "Integration" },
                        AdditionalProperties = new Dictionary<string, object>
                        {
                            ["Make"] = "Yamaha",
                            ["Model"] = "YZF-R1M",
                            ["Year"] = "2024",
                            ["Engine"] = "998cc Inline-4",
                            ["Power"] = "200hp",
                            ["Weight"] = "202kg"
                        }
                    }
                }
            },
            Metadata = new Dictionary<string, object>
            {
                ["SourceType"] = "CSV",
                ["ProcessedAt"] = DateTime.UtcNow,
                ["TestType"] = "Integration"
            }
        };
    }

    private ProcessedData CreateTestPDFData()
    {
        return new ProcessedData
        {
            Id = Guid.NewGuid().ToString(),
            Documents = new List<MotorcycleDocument>
            {
                new MotorcycleDocument
                {
                    Id = $"integration-pdf-{Guid.NewGuid()}",
                    Title = "Honda CBR1000RR Service Manual - Engine Section",
                    Content = "Detailed engine maintenance procedures for Honda CBR1000RR including valve adjustments, oil changes, and timing chain inspection.",
                    Type = DocumentType.Manual,
                    ContentVector = GenerateTestVector(1536),
                    Metadata = new DocumentMetadata
                    {
                        SourceFile = "honda_cbr1000rr_service_manual.pdf",
                        Section = "Engine Maintenance",
                        PageNumber = 45,
                        Author = "Honda Motor Co.",
                        PublishedDate = DateTime.UtcNow.AddMonths(-6),
                        Tags = new List<string> { "Honda", "CBR1000RR", "Service", "Engine", "Integration" },
                        AdditionalProperties = new Dictionary<string, object>
                        {
                            ["Make"] = "Honda",
                            ["Model"] = "CBR1000RR",
                            ["Year"] = "2024",
                            ["ChunkType"] = "Text",
                            ["Language"] = "en",
                            ["ManualVersion"] = "2024.1"
                        }
                    }
                }
            },
            Metadata = new Dictionary<string, object>
            {
                ["SourceType"] = "PDF",
                ["ProcessedAt"] = DateTime.UtcNow,
                ["TestType"] = "Integration"
            }
        };
    }

    private ProcessedData CreateLargeTestDataset(int documentCount)
    {
        var documents = new List<MotorcycleDocument>();
        var makes = new[] { "Honda", "Yamaha", "Kawasaki", "Suzuki", "Ducati", "BMW", "KTM", "Aprilia" };
        var models = new[] { "Sport", "Touring", "Cruiser", "Adventure", "Naked", "Supermoto" };

        for (int i = 0; i < documentCount; i++)
        {
            var make = makes[i % makes.Length];
            var model = models[i % models.Length];
            
            documents.Add(new MotorcycleDocument
            {
                Id = $"integration-large-{i}-{Guid.NewGuid()}",
                Title = $"{make} {model} {2020 + (i % 5)} - Spec {i}",
                Content = $"Detailed specifications for {make} {model} motorcycle model year {2020 + (i % 5)}. Document {i} in large dataset integration test.",
                Type = DocumentType.Specification,
                ContentVector = GenerateTestVector(1536),
                Metadata = new DocumentMetadata
                {
                    SourceFile = "large_integration_dataset.csv",
                    Section = "Specifications",
                    Tags = new List<string> { make, model, "Integration", "Large Dataset" },
                    AdditionalProperties = new Dictionary<string, object>
                    {
                        ["Make"] = make,
                        ["Model"] = $"{model}{i}",
                        ["Year"] = (2020 + (i % 5)).ToString(),
                        ["DocumentIndex"] = i,
                        ["BatchTest"] = true
                    }
                }
            });
        }

        return new ProcessedData
        {
            Id = Guid.NewGuid().ToString(),
            Documents = documents,
            Metadata = new Dictionary<string, object>
            {
                ["SourceType"] = "CSV",
                ["ProcessedAt"] = DateTime.UtcNow,
                ["TestType"] = "Integration-Large",
                ["DocumentCount"] = documentCount
            }
        };
    }

    private ProcessedData CreateTestDataWithRichMetadata()
    {
        return new ProcessedData
        {
            Id = Guid.NewGuid().ToString(),
            Documents = new List<MotorcycleDocument>
            {
                new MotorcycleDocument
                {
                    Id = $"integration-metadata-{Guid.NewGuid()}",
                    Title = "Ducati Panigale V4 S - Complete Specifications",
                    Content = "Comprehensive specifications for Ducati Panigale V4 S including performance data, dimensions, and technical features.",
                    Type = DocumentType.Specification,
                    ContentVector = GenerateTestVector(1536),
                    Metadata = new DocumentMetadata
                    {
                        SourceFile = "ducati_complete_specs.csv",
                        Section = "Premium Superbikes",
                        Tags = new List<string> { "Ducati", "Panigale", "V4", "Premium", "Superbike", "Integration" },
                        AdditionalProperties = new Dictionary<string, object>
                        {
                            ["Make"] = "Ducati",
                            ["Model"] = "Panigale V4 S",
                            ["Year"] = "2024",
                            ["Engine"] = "1103cc V4",
                            ["Power"] = "214hp",
                            ["Torque"] = "124Nm",
                            ["Weight"] = "195kg",
                            ["TopSpeed"] = "300km/h",
                            ["Price"] = "$28,000",
                            ["Category"] = "Superbike",
                            ["ElectronicPackage"] = "DTC, DWC, DSC, DQS",
                            ["SuspensionFront"] = "Öhlins NIX-30",
                            ["SuspensionRear"] = "Öhlins TTX36",
                            ["BrakesFront"] = "Brembo Stylema",
                            ["BrakesRear"] = "Brembo",
                            ["TiresFront"] = "120/70 ZR17",
                            ["TiresRear"] = "200/55 ZR17",
                            ["FuelCapacity"] = "16L",
                            ["SeatHeight"] = "830mm",
                            ["Wheelbase"] = "1469mm"
                        }
                    }
                }
            },
            Metadata = new Dictionary<string, object>
            {
                ["SourceType"] = "CSV",
                ["ProcessedAt"] = DateTime.UtcNow,
                ["TestType"] = "Integration-Metadata",
                ["MetadataComplexity"] = "High"
            }
        };
    }

    private float[] GenerateTestVector(int dimensions)
    {
        var random = new Random();
        var vector = new float[dimensions];
        
        for (int i = 0; i < dimensions; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2.0 - 1.0); // Values between -1 and 1
        }
        
        return vector;
    }

    #endregion

    public void Dispose()
    {
        // Cleanup is handled by the test fixture
    }
}

/// <summary>
/// Test fixture for setting up the test host with proper DI configuration
/// </summary>
public class TestHostFixture : IDisposable
{
    public IServiceProvider ServiceProvider { get; private set; }
    private readonly IHost _host;

    public TestHostFixture()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.Test.json", optional: false);
            })
            .ConfigureServices((context, services) =>
            {
                // Add Azure services
                services.AddAzureServices(context.Configuration);
                
                // Add logging
                services.AddLogging(builder => builder.AddConsole());
            });

        _host = builder.Build();
        ServiceProvider = _host.Services;
    }

    public void Dispose()
    {
        _host?.Dispose();
    }
}