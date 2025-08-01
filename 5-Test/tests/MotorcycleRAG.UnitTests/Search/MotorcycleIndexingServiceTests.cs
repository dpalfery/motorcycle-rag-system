using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using MotorcycleRAG.Infrastructure.Search;
using Xunit;

namespace MotorcycleRAG.UnitTests.Search;

/// <summary>
/// Unit tests for MotorcycleIndexingService
/// Tests index creation, batch indexing, and metadata management
/// </summary>
public class MotorcycleIndexingServiceTests : IDisposable
{
    private readonly Mock<SearchIndexClient> _mockIndexClient;
    private readonly Mock<IAzureSearchClient> _mockSearchClient;
    private readonly Mock<ILogger<MotorcycleIndexingService>> _mockLogger;
    private readonly IOptions<SearchConfiguration> _searchConfig;
    private readonly MotorcycleIndexingService _indexingService;

    public MotorcycleIndexingServiceTests()
    {
        _mockIndexClient = new Mock<SearchIndexClient>();
        _mockSearchClient = new Mock<IAzureSearchClient>();
        _mockLogger = new Mock<ILogger<MotorcycleIndexingService>>();
        
        _searchConfig = Options.Create(new SearchConfiguration
        {
            IndexName = "test-motorcycle-index",
            BatchSize = 100,
            MaxSearchResults = 50,
            EnableHybridSearch = true,
            EnableSemanticRanking = true
        });

        _indexingService = new MotorcycleIndexingService(
            _mockIndexClient.Object,
            _mockSearchClient.Object,
            _searchConfig,
            _mockLogger.Object);
    }

    [Fact]
    public async Task IndexCSVDataAsync_ShouldIndexDocumentsInBatches_WhenDataProvided()
    {
        // Arrange
        var processedData = CreateTestCSVProcessedData();
        
        _mockSearchClient.Setup(x => x.IndexDocumentsAsync(
            It.IsAny<object[]>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _indexingService.IndexCSVDataAsync(processedData);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.DocumentsIndexed > 0);
        Assert.Empty(result.Errors);
        Assert.Equal("motorcycle-csv-index", result.IndexName);
        
        // Verify batch indexing was called
        _mockSearchClient.Verify(x => x.IndexDocumentsAsync(
            It.IsAny<object[]>(),
            It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task IndexPDFDataAsync_ShouldIndexDocumentsInBatches_WhenDataProvided()
    {
        // Arrange
        var processedData = CreateTestPDFProcessedData();
        
        _mockSearchClient.Setup(x => x.IndexDocumentsAsync(
            It.IsAny<object[]>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _indexingService.IndexPDFDataAsync(processedData);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.DocumentsIndexed > 0);
        Assert.Empty(result.Errors);
        Assert.Equal("motorcycle-pdf-index", result.IndexName);
        
        // Verify batch indexing was called
        _mockSearchClient.Verify(x => x.IndexDocumentsAsync(
            It.IsAny<object[]>(),
            It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task IndexCSVDataAsync_ShouldHandleBatchFailures_WhenIndexingFails()
    {
        // Arrange
        var processedData = CreateTestCSVProcessedData();
        
        _mockSearchClient.Setup(x => x.IndexDocumentsAsync(
            It.IsAny<object[]>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _indexingService.IndexCSVDataAsync(processedData);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0, result.DocumentsIndexed);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task IndexPDFDataAsync_ShouldHandleBatchFailures_WhenIndexingFails()
    {
        // Arrange
        var processedData = CreateTestPDFProcessedData();
        
        _mockSearchClient.Setup(x => x.IndexDocumentsAsync(
            It.IsAny<object[]>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _indexingService.IndexPDFDataAsync(processedData);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0, result.DocumentsIndexed);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task IndexCSVDataAsync_ShouldRespectBatchSizeConfiguration_WhenProcessingLargeDataset()
    {
        // Arrange
        var largeDataset = CreateLargeCSVProcessedData(250); // More than batch size
        
        _mockSearchClient.Setup(x => x.IndexDocumentsAsync(
            It.IsAny<object[]>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _indexingService.IndexCSVDataAsync(largeDataset);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.DocumentsIndexed > 0);
        
        // Verify multiple batch calls were made - should respect batch size limit
        _mockSearchClient.Verify(x => x.IndexDocumentsAsync(
            It.Is<object[]>(docs => docs.Length <= 100), // Respects batch size
            It.IsAny<CancellationToken>()), Times.AtLeast(6));
    }

    [Fact]
    public async Task IndexCSVDataAsync_ShouldFilterDocumentsByType_WhenMixedTypesProvided()
    {
        // Arrange
        var mixedData = CreateMixedTypeProcessedData();
        
        _mockSearchClient.Setup(x => x.IndexDocumentsAsync(
            It.IsAny<object[]>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _indexingService.IndexCSVDataAsync(mixedData);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.DocumentsIndexed > 0);
        
        // Verify only specification documents were indexed
        _mockSearchClient.Verify(x => x.IndexDocumentsAsync(
            It.Is<object[]>(docs => docs.Length == 1), // Only CSV docs
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenRequiredParametersAreNull()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MotorcycleIndexingService(
            null!, _mockSearchClient.Object, _searchConfig, _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() => new MotorcycleIndexingService(
            _mockIndexClient.Object, null!, _searchConfig, _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() => new MotorcycleIndexingService(
            _mockIndexClient.Object, _mockSearchClient.Object, null!, _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() => new MotorcycleIndexingService(
            _mockIndexClient.Object, _mockSearchClient.Object, _searchConfig, null!));
    }

    [Fact]
    public async Task IndexCSVDataAsync_ShouldHandleEmptyDocumentList_WhenNoDocumentsProvided()
    {
        // Arrange
        var emptyData = new ProcessedData
        {
            Id = Guid.NewGuid().ToString(),
            Documents = new List<MotorcycleDocument>(),
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await _indexingService.IndexCSVDataAsync(emptyData);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.DocumentsIndexed);
        Assert.Empty(result.Errors);
    }

    #region Test Data Creation Methods

    private ProcessedData CreateTestCSVProcessedData()
    {
        return new ProcessedData
        {
            Id = Guid.NewGuid().ToString(),
            Documents = new List<MotorcycleDocument>
            {
                new MotorcycleDocument
                {
                    Id = "csv-1",
                    Title = "Honda CBR600RR Specifications",
                    Content = "Honda CBR600RR 2023 specifications and features",
                    Type = DocumentType.Specification,
                    ContentVector = new float[] { 0.1f, 0.2f, 0.3f },
                    Metadata = new DocumentMetadata
                    {
                        SourceFile = "honda_specs.csv",
                        Section = "Specifications",
                        AdditionalProperties = new Dictionary<string, object>
                        {
                            ["Make"] = "Honda",
                            ["Model"] = "CBR600RR",
                            ["Year"] = "2023"
                        }
                    }
                },
                new MotorcycleDocument
                {
                    Id = "csv-2",
                    Title = "Yamaha R6 Specifications",
                    Content = "Yamaha R6 2023 specifications and features",
                    Type = DocumentType.Specification,
                    ContentVector = new float[] { 0.4f, 0.5f, 0.6f },
                    Metadata = new DocumentMetadata
                    {
                        SourceFile = "yamaha_specs.csv",
                        Section = "Specifications",
                        AdditionalProperties = new Dictionary<string, object>
                        {
                            ["Make"] = "Yamaha",
                            ["Model"] = "R6",
                            ["Year"] = "2023"
                        }
                    }
                },
                new MotorcycleDocument
                {
                    Id = "csv-3",
                    Title = "Kawasaki ZX-6R Specifications",
                    Content = "Kawasaki ZX-6R 2023 specifications and features",
                    Type = DocumentType.Specification,
                    ContentVector = new float[] { 0.7f, 0.8f, 0.9f },
                    Metadata = new DocumentMetadata
                    {
                        SourceFile = "kawasaki_specs.csv",
                        Section = "Specifications",
                        AdditionalProperties = new Dictionary<string, object>
                        {
                            ["Make"] = "Kawasaki",
                            ["Model"] = "ZX-6R",
                            ["Year"] = "2023"
                        }
                    }
                }
            },
            Metadata = new Dictionary<string, object>
            {
                ["SourceType"] = "CSV",
                ["ProcessedAt"] = DateTime.UtcNow
            }
        };
    }

    private ProcessedData CreateTestPDFProcessedData()
    {
        return new ProcessedData
        {
            Id = Guid.NewGuid().ToString(),
            Documents = new List<MotorcycleDocument>
            {
                new MotorcycleDocument
                {
                    Id = "pdf-1",
                    Title = "Honda CBR600RR Service Manual - Chapter 1",
                    Content = "Service procedures for Honda CBR600RR engine maintenance",
                    Type = DocumentType.Manual,
                    ContentVector = new float[] { 0.1f, 0.2f, 0.3f },
                    Metadata = new DocumentMetadata
                    {
                        SourceFile = "honda_service_manual.pdf",
                        Section = "Engine Maintenance",
                        PageNumber = 1,
                        AdditionalProperties = new Dictionary<string, object>
                        {
                            ["Make"] = "Honda",
                            ["Model"] = "CBR600RR",
                            ["Year"] = "2023",
                            ["ChunkType"] = "Text",
                            ["Language"] = "en"
                        }
                    }
                },
                new MotorcycleDocument
                {
                    Id = "pdf-2",
                    Title = "Honda CBR600RR Service Manual - Chapter 2",
                    Content = "Electrical system troubleshooting for Honda CBR600RR",
                    Type = DocumentType.Manual,
                    ContentVector = new float[] { 0.4f, 0.5f, 0.6f },
                    Metadata = new DocumentMetadata
                    {
                        SourceFile = "honda_service_manual.pdf",
                        Section = "Electrical System",
                        PageNumber = 15,
                        AdditionalProperties = new Dictionary<string, object>
                        {
                            ["Make"] = "Honda",
                            ["Model"] = "CBR600RR",
                            ["Year"] = "2023",
                            ["ChunkType"] = "Text",
                            ["Language"] = "en"
                        }
                    }
                }
            },
            Metadata = new Dictionary<string, object>
            {
                ["SourceType"] = "PDF",
                ["ProcessedAt"] = DateTime.UtcNow
            }
        };
    }

    private ProcessedData CreateLargeCSVProcessedData(int documentCount)
    {
        var documents = new List<MotorcycleDocument>();
        
        for (int i = 0; i < documentCount; i++)
        {
            documents.Add(new MotorcycleDocument
            {
                Id = $"csv-large-{i}",
                Title = $"Motorcycle Spec {i}",
                Content = $"Specifications for motorcycle {i}",
                Type = DocumentType.Specification,
                ContentVector = new float[] { i * 0.1f, i * 0.2f, i * 0.3f },
                Metadata = new DocumentMetadata
                {
                    SourceFile = "large_specs.csv",
                    Section = "Specifications",
                    AdditionalProperties = new Dictionary<string, object>
                    {
                        ["Make"] = $"Make{i % 10}",
                        ["Model"] = $"Model{i}",
                        ["Year"] = "2023"
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
                ["ProcessedAt"] = DateTime.UtcNow
            }
        };
    }

    private ProcessedData CreateMixedTypeProcessedData()
    {
        return new ProcessedData
        {
            Id = Guid.NewGuid().ToString(),
            Documents = new List<MotorcycleDocument>
            {
                new MotorcycleDocument
                {
                    Id = "mixed-csv-1",
                    Title = "CSV Specification",
                    Content = "CSV specification content",
                    Type = DocumentType.Specification,
                    ContentVector = new float[] { 0.1f, 0.2f, 0.3f },
                    Metadata = new DocumentMetadata
                    {
                        SourceFile = "mixed.csv",
                        AdditionalProperties = new Dictionary<string, object>
                        {
                            ["Make"] = "Honda",
                            ["Model"] = "CBR",
                            ["Year"] = "2023"
                        }
                    }
                },
                new MotorcycleDocument
                {
                    Id = "mixed-pdf-1",
                    Title = "PDF Manual",
                    Content = "PDF manual content",
                    Type = DocumentType.Manual,
                    ContentVector = new float[] { 0.4f, 0.5f, 0.6f },
                    Metadata = new DocumentMetadata
                    {
                        SourceFile = "mixed.pdf",
                        AdditionalProperties = new Dictionary<string, object>
                        {
                            ["Make"] = "Honda",
                            ["Model"] = "CBR",
                            ["Year"] = "2023"
                        }
                    }
                }
            },
            Metadata = new Dictionary<string, object>
            {
                ["SourceType"] = "Mixed",
                ["ProcessedAt"] = DateTime.UtcNow
            }
        };
    }

    #endregion

    public void Dispose()
    {
        _indexingService?.Dispose();
    }
}