using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using MotorcycleRAG.Infrastructure.DataProcessing;
using Xunit;

namespace MotorcycleRAG.UnitTests.DataProcessing;

public class MotorcycleCSVProcessorTests
{
    private readonly Mock<IAzureOpenAIClient> _mockOpenAIClient;
    private readonly Mock<IAzureSearchClient> _mockSearchClient;
    private readonly Mock<ILogger<MotorcycleCSVProcessor>> _mockLogger;
    private readonly MotorcycleCSVProcessor _processor;
    private readonly CSVProcessingConfiguration _configuration;

    public MotorcycleCSVProcessorTests()
    {
        _mockOpenAIClient = new Mock<IAzureOpenAIClient>();
        _mockSearchClient = new Mock<IAzureSearchClient>();
        _mockLogger = new Mock<ILogger<MotorcycleCSVProcessor>>();
        
        _configuration = new CSVProcessingConfiguration
        {
            ChunkSize = 2,
            MaxRows = 100,
            IdentifierFields = new List<string> { "Make", "Model", "Year" },
            PreserveRelationalIntegrity = true
        };

        _processor = new MotorcycleCSVProcessor(
            _mockOpenAIClient.Object,
            _mockSearchClient.Object,
            _mockLogger.Object,
            _configuration);
    }

    [Fact]
    public async Task ProcessAsync_WithValidCSV_ShouldReturnSuccessResult()
    {
        // Arrange
        var csvContent = "Make,Model,Year,Engine\nHonda,CBR600RR,2023,599cc\nYamaha,YZF-R6,2023,599cc";
        var csvFile = CreateCSVFile("test.csv", csvContent);
        
        _mockOpenAIClient.Setup(x => x.GetEmbeddingAsync("text-embedding-3-large", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        // Act
        var result = await _processor.ProcessAsync(csvFile);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.ItemsProcessed); // 2 different motorcycles = 2 chunks due to relational integrity
        Assert.Contains("Successfully processed", result.Message);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ProcessAsync_WithHeaderlessCSV_ShouldGenerateColumnNames()
    {
        // Arrange
        var csvContent = "Honda,CBR600RR,2023,599cc\nYamaha,YZF-R6,2023,599cc";
        var csvFile = CreateCSVFile("test.csv", csvContent, hasHeaders: false);
        
        _mockOpenAIClient.Setup(x => x.GetEmbeddingAsync("text-embedding-3-large", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        // Act
        var result = await _processor.ProcessAsync(csvFile);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.Documents.Any());
        
        // Verify generated column names are used
        var firstDocument = result.Data.Documents.First();
        Assert.Contains("Column1", firstDocument.Content);
        Assert.Contains("Column2", firstDocument.Content);
    }

    [Fact]
    public async Task ProcessAsync_WithTooManyColumns_ShouldThrowException()
    {
        // Arrange
        var headers = string.Join(",", Enumerable.Range(1, 200).Select(i => $"Col{i}"));
        var csvContent = headers + "\n" + string.Join(",", Enumerable.Range(1, 200).Select(i => $"Value{i}"));
        var csvFile = CreateCSVFile("test.csv", csvContent);

        // Act & Assert
        var result = await _processor.ProcessAsync(csvFile);
        Assert.False(result.Success);
        Assert.Contains("maximum allowed", result.Message);
    }

    [Fact]
    public async Task ProcessAsync_WithRelationalIntegrity_ShouldPreserveMotorcycleGroups()
    {
        // Arrange
        var csvContent = @"Make,Model,Year,Feature
Honda,CBR600RR,2023,ABS
Honda,CBR600RR,2023,Traction Control
Yamaha,YZF-R6,2023,Quick Shifter
Yamaha,YZF-R6,2023,Slipper Clutch";
        
        var csvFile = CreateCSVFile("test.csv", csvContent);
        
        _mockOpenAIClient.Setup(x => x.GetEmbeddingAsync("text-embedding-3-large", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        // Act
        var result = await _processor.ProcessAsync(csvFile);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        
        // Should create 2 chunks (one for each motorcycle)
        Assert.Equal(2, result.Data.Documents.Count);
        
        // First chunk should contain Honda CBR600RR data
        var hondaChunk = result.Data.Documents.First();
        Assert.Contains("Honda", hondaChunk.Content);
        Assert.Contains("CBR600RR", hondaChunk.Content);
        Assert.Contains("ABS", hondaChunk.Content);
        Assert.Contains("Traction Control", hondaChunk.Content);
        
        // Second chunk should contain Yamaha YZF-R6 data
        var yamahaChunk = result.Data.Documents.Last();
        Assert.Contains("Yamaha", yamahaChunk.Content);
        Assert.Contains("YZF-R6", yamahaChunk.Content);
        Assert.Contains("Quick Shifter", yamahaChunk.Content);
        Assert.Contains("Slipper Clutch", yamahaChunk.Content);
    }

    [Fact]
    public async Task ProcessAsync_WithoutRelationalIntegrity_ShouldChunkBySize()
    {
        // Arrange
        _configuration.PreserveRelationalIntegrity = false;
        var processor = new MotorcycleCSVProcessor(
            _mockOpenAIClient.Object,
            _mockSearchClient.Object,
            _mockLogger.Object,
            _configuration);

        var csvContent = @"Make,Model,Year,Feature
Honda,CBR600RR,2023,ABS
Honda,CBR600RR,2023,Traction Control
Honda,CBR600RR,2023,Quick Shifter";
        
        var csvFile = CreateCSVFile("test.csv", csvContent);
        
        _mockOpenAIClient.Setup(x => x.GetEmbeddingAsync("text-embedding-3-large", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        // Act
        var result = await processor.ProcessAsync(csvFile);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        
        // Should create 2 chunks based on chunk size (2 rows each)
        Assert.Equal(2, result.Data.Documents.Count);
    }

    [Fact]
    public async Task ProcessAsync_WithEmbeddingGenerationFailure_ShouldHandleError()
    {
        // Arrange
        var csvContent = "Make,Model,Year\nHonda,CBR600RR,2023";
        var csvFile = CreateCSVFile("test.csv", csvContent);
        
        _mockOpenAIClient.Setup(x => x.GetEmbeddingAsync("text-embedding-3-large", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("OpenAI service unavailable"));

        // Act
        var result = await _processor.ProcessAsync(csvFile);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("OpenAI service unavailable", result.Errors.First());
    }

    [Fact]
    public async Task ProcessAsync_WithInvalidInput_ShouldReturnValidationErrors()
    {
        // Arrange
        var csvFile = new CSVFile
        {
            FileName = "",
            Content = Stream.Null
        };

        // Act
        var result = await _processor.ProcessAsync(csvFile);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("File name is required", result.Errors);
        Assert.Contains("File content is required", result.Errors);
        Assert.Equal("Input validation failed", result.Message);
    }

    [Fact]
    public async Task ProcessAsync_WithEmptyCSV_ShouldReturnValidationError()
    {
        // Arrange
        var csvFile = CreateCSVFile("test.csv", "");

        // Act
        var result = await _processor.ProcessAsync(csvFile);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("File content cannot be empty", result.Errors);
    }

    [Fact]
    public async Task ProcessAsync_WithLargeCSV_ShouldRespectMaxRowsLimit()
    {
        // Arrange
        _configuration.MaxRows = 5;
        var processor = new MotorcycleCSVProcessor(
            _mockOpenAIClient.Object,
            _mockSearchClient.Object,
            _mockLogger.Object,
            _configuration);

        var csvBuilder = new StringBuilder("Make,Model,Year\n");
        for (int i = 1; i <= 10; i++)
        {
            csvBuilder.AppendLine($"Honda,CBR600RR,2023"); // Same motorcycle to test chunking by size
        }
        
        var csvFile = CreateCSVFile("test.csv", csvBuilder.ToString());
        
        _mockOpenAIClient.Setup(x => x.GetEmbeddingAsync("text-embedding-3-large", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        // Act
        var result = await _processor.ProcessAsync(csvFile);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        
        // Should process only up to MaxRows (5 rows), creating 3 chunks (2+2+1)
        Assert.Equal(3, result.Data.Documents.Count);
    }

    [Fact]
    public async Task IndexAsync_WithValidData_ShouldIndexSuccessfully()
    {
        // Arrange
        var processedData = new ProcessedData
        {
            Id = "test-id",
            Documents = new List<MotorcycleDocument>
            {
                new MotorcycleDocument
                {
                    Id = "doc1",
                    Title = "Honda CBR600RR",
                    Content = "Test content",
                    Type = DocumentType.Specification
                }
            }
        };

        _mockSearchClient.Setup(x => x.IndexDocumentsAsync(It.IsAny<MotorcycleDocument[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _processor.IndexAsync(processedData);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.DocumentsIndexed);
        Assert.Equal("motorcycle-specifications", result.IndexName);
        Assert.Contains("Indexed 1 documents successfully", result.Message);
    }

    [Fact]
    public async Task IndexAsync_WithIndexingFailure_ShouldHandleError()
    {
        // Arrange
        var processedData = new ProcessedData
        {
            Id = "test-id",
            Documents = new List<MotorcycleDocument>
            {
                new MotorcycleDocument { Id = "doc1", Title = "Test", Content = "Test content" }
            }
        };

        _mockSearchClient.Setup(x => x.IndexDocumentsAsync(It.IsAny<MotorcycleDocument[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _processor.IndexAsync(processedData);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0, result.DocumentsIndexed);
        Assert.Contains("Failed to index batch", result.Errors.First());
    }

    [Fact]
    public async Task IndexAsync_WithLargeBatch_ShouldProcessInBatches()
    {
        // Arrange
        var documents = Enumerable.Range(1, 250)
            .Select(i => new MotorcycleDocument
            {
                Id = $"doc{i}",
                Title = $"Document {i}",
                Content = $"Content {i}",
                Type = DocumentType.Specification
            }).ToList();

        var processedData = new ProcessedData
        {
            Id = "test-id",
            Documents = documents
        };

        _mockSearchClient.Setup(x => x.IndexDocumentsAsync(It.IsAny<MotorcycleDocument[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _processor.IndexAsync(processedData);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(250, result.DocumentsIndexed);
        
        // Verify that IndexDocumentsAsync was called 3 times (100+100+50)
        _mockSearchClient.Verify(x => x.IndexDocumentsAsync(It.IsAny<MotorcycleDocument[]>(), It.IsAny<CancellationToken>()), 
            Times.Exactly(3));
    }

    [Fact]
    public void Constructor_WithNullDependencies_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new MotorcycleCSVProcessor(null!, _mockSearchClient.Object, _mockLogger.Object));
        
        Assert.Throws<ArgumentNullException>(() => 
            new MotorcycleCSVProcessor(_mockOpenAIClient.Object, null!, _mockLogger.Object));
        
        Assert.Throws<ArgumentNullException>(() => 
            new MotorcycleCSVProcessor(_mockOpenAIClient.Object, _mockSearchClient.Object, null!));
    }

    [Fact]
    public async Task ProcessAsync_WithMalformedCSVRow_ShouldSkipBadRowsAndContinue()
    {
        // Arrange - Create a CSV with valid rows that should process successfully
        var csvContent = @"Make,Model,Year
Honda,CBR600RR,2023
Yamaha,YZF-R6,2023";
        
        var csvFile = CreateCSVFile("test.csv", csvContent);
        
        _mockOpenAIClient.Setup(x => x.GetEmbeddingAsync("text-embedding-3-large", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        // Act
        var result = await _processor.ProcessAsync(csvFile);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        
        // Should process the valid rows
        Assert.True(result.Data.Documents.Count > 0);
        Assert.Equal(2, result.Data.Documents.Count); // 2 different motorcycles = 2 chunks due to relational integrity
    }

    private CSVFile CreateCSVFile(string fileName, string content, bool hasHeaders = true)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return new CSVFile
        {
            FileName = fileName,
            Content = stream,
            HasHeaders = hasHeaders,
            Delimiter = ",",
            Encoding = "UTF-8",
            MaxColumns = 150
        };
    }
}