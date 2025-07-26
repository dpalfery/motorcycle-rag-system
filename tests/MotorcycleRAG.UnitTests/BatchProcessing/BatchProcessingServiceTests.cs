using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using MotorcycleRAG.Infrastructure.BatchProcessing;
using Xunit;

namespace MotorcycleRAG.UnitTests.BatchProcessing;

public class BatchProcessingServiceTests
{
    private readonly Mock<ILogger<BatchProcessingService>> _mockLogger;
    private readonly Mock<IAzureOpenAIClient> _mockOpenAIClient;
    private readonly Mock<IMotorcycleIndexingService> _mockIndexingService;
    private readonly BatchProcessingService _batchProcessingService;
    private readonly BatchProcessingConfiguration _config;

    public BatchProcessingServiceTests()
    {
        _mockLogger = new Mock<ILogger<BatchProcessingService>>();
        _mockOpenAIClient = new Mock<IAzureOpenAIClient>();
        _mockIndexingService = new Mock<IMotorcycleIndexingService>();
        
        _config = new BatchProcessingConfiguration
        {
            DefaultBatchSize = 10,
            MaxBatchSize = 100,
            MaxMemoryPerBatchMB = 50,
            ParallelProcessingThreads = 2,
            BatchProcessingTimeout = TimeSpan.FromMinutes(5),
            EnableOptimization = true,
            MaxRetryAttempts = 3
        };

        var options = Options.Create(_config);
        _batchProcessingService = new BatchProcessingService(
            _mockLogger.Object, 
            options, 
            _mockOpenAIClient.Object, 
            _mockIndexingService.Object);
    }

    [Fact]
    public async Task ProcessDocumentBatchAsync_WithValidDocuments_ProcessesSuccessfully()
    {
        // Arrange
        var documents = new List<ProcessingDocument>
        {
            new() { Id = "1", Content = "Test content 1", DocumentType = "specification" },
            new() { Id = "2", Content = "Test content 2", DocumentType = "manual" },
            new() { Id = "3", Content = "Test content 3", DocumentType = "specification" }
        };

        // Act
        var result = await _batchProcessingService.ProcessDocumentBatchAsync(documents, 2);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalDocuments);
        Assert.Equal(3, result.SuccessfullyProcessed);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Errors);
        Assert.True(result.ProcessingTime > TimeSpan.Zero);
        Assert.True(result.ThroughputPerSecond > 0);
    }

    [Fact]
    public async Task ProcessDocumentBatchAsync_WithEmptyDocuments_ReturnsEmptyResult()
    {
        // Arrange
        var documents = new List<ProcessingDocument>();

        // Act
        var result = await _batchProcessingService.ProcessDocumentBatchAsync(documents, 10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalDocuments);
        Assert.Equal(0, result.SuccessfullyProcessed);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ProcessEmbeddingBatchAsync_WithValidTexts_GeneratesEmbeddings()
    {
        // Arrange
        var texts = new List<string>
        {
            "motorcycle specifications",
            "engine performance",
            "maintenance schedule"
        };

        // Setup mock to return embeddings based on input batch size
        _mockOpenAIClient
            .Setup(x => x.GetEmbeddingsAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string deployment, string[] inputTexts, CancellationToken token) =>
            {
                // Return embeddings matching the number of input texts
                var embeddings = new float[inputTexts.Length][];
                for (int i = 0; i < inputTexts.Length; i++)
                {
                    embeddings[i] = new float[1536];
                }
                return embeddings;
            });

        // Act
        var result = await _batchProcessingService.ProcessEmbeddingBatchAsync(texts, 2);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        Assert.All(result, embedding => Assert.Equal(1536, embedding.Length));

        _mockOpenAIClient.Verify(
            x => x.GetEmbeddingsAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessEmbeddingBatchAsync_WithApiFailure_HandlesGracefully()
    {
        // Arrange
        var texts = new List<string> { "test text" };

        _mockOpenAIClient
            .Setup(x => x.GetEmbeddingsAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API Error"));

        // Act
        var result = await _batchProcessingService.ProcessEmbeddingBatchAsync(texts, 1);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(1536, result[0].Length); // Default empty embedding
        Assert.All(result[0], value => Assert.Equal(0f, value));
    }

    [Fact]
    public async Task IndexDocumentBatchAsync_WithValidDocuments_IndexesSuccessfully()
    {
        // Arrange
        var documents = new List<MotorcycleDocument>
        {
            new() { Id = "1", Content = "Test content 1" },
            new() { Id = "2", Content = "Test content 2" }
        };

        _mockIndexingService
            .Setup(x => x.IndexCSVDataAsync(It.IsAny<ProcessedData>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexingResult { Success = true, DocumentsIndexed = 2 });

        // Act
        var result = await _batchProcessingService.IndexDocumentBatchAsync(documents, 2);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalDocuments);
        Assert.Equal(2, result.SuccessfullyIndexed);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Errors);
        Assert.True(result.IndexingTime > TimeSpan.Zero);

        _mockIndexingService.Verify(
            x => x.IndexCSVDataAsync(It.IsAny<ProcessedData>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IndexDocumentBatchAsync_WithIndexingFailure_RecordsErrors()
    {
        // Arrange
        var documents = new List<MotorcycleDocument>
        {
            new() { Id = "1", Content = "Test content 1" },
            new() { Id = "2", Content = "Test content 2" }
        };

        _mockIndexingService
            .Setup(x => x.IndexCSVDataAsync(It.IsAny<ProcessedData>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Indexing failed"));

        // Act
        var result = await _batchProcessingService.IndexDocumentBatchAsync(documents, 2);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalDocuments);
        Assert.Equal(0, result.SuccessfullyIndexed);
        Assert.Equal(2, result.Failed);
        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, error => Assert.Equal("INDEXING_FAILED", error.ErrorCode));
    }

    [Theory]
    [InlineData(1024, 1000000, 10)] // Small docs, lots of memory
    [InlineData(1048576, 52428800, 50)] // 1MB docs, 50MB memory limit
    [InlineData(10240, 10485760, 10)] // 10KB docs, 10MB memory limit
    public void GetOptimalBatchSize_WithDifferentConstraints_ReturnsReasonableSize(
        long avgDocSize, 
        long availableMemory, 
        int expectedMinSize)
    {
        // Act
        var batchSize = _batchProcessingService.GetOptimalBatchSize(avgDocSize, availableMemory);

        // Assert
        Assert.True(batchSize >= expectedMinSize, $"Expected batch size >= {expectedMinSize}, got {batchSize}");
        Assert.True(batchSize <= _config.MaxBatchSize, $"Expected batch size <= {_config.MaxBatchSize}, got {batchSize}");
        Assert.True(batchSize >= 10, $"Expected batch size >= 10, got {batchSize}");
    }

    [Fact]
    public void GetOptimalBatchSize_WithZeroDocumentSize_ReturnsDefaultSize()
    {
        // Act
        var batchSize = _batchProcessingService.GetOptimalBatchSize(0, 1000000);

        // Assert
        Assert.True(batchSize > 0);
        Assert.True(batchSize <= _config.MaxBatchSize);
    }

    [Fact]
    public async Task ProcessDocumentBatchAsync_WithCancellation_ThrowsOperationCancelledException()
    {
        // Arrange
        var documents = new List<ProcessingDocument>
        {
            new() { Id = "1", Content = "Test content" }
        };

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _batchProcessingService.ProcessDocumentBatchAsync(documents, 1, cancellationTokenSource.Token);
        });
        
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task ProcessEmbeddingBatchAsync_WithEmptyTexts_ReturnsEmptyArray()
    {
        // Arrange
        var texts = new List<string>();

        // Act
        var result = await _batchProcessingService.ProcessEmbeddingBatchAsync(texts, 10);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ProcessDocumentBatchAsync_WithLargeBatch_UsesOptimalBatchSize()
    {
        // Arrange
        var documents = new List<ProcessingDocument>();
        for (int i = 0; i < 200; i++)
        {
            documents.Add(new ProcessingDocument 
            { 
                Id = i.ToString(), 
                Content = $"Test content {i}", 
                DocumentType = "test" 
            });
        }

        // Act
        var result = await _batchProcessingService.ProcessDocumentBatchAsync(documents, 1000); // Request large batch

        // Assert
        Assert.NotNull(result);
        Assert.Equal(200, result.TotalDocuments);
        Assert.Equal(200, result.SuccessfullyProcessed);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task IndexDocumentBatchAsync_WithEmptyDocuments_ReturnsEmptyResult()
    {
        // Arrange
        var documents = new List<MotorcycleDocument>();

        // Act
        var result = await _batchProcessingService.IndexDocumentBatchAsync(documents, 10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalDocuments);
        Assert.Equal(0, result.SuccessfullyIndexed);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ProcessEmbeddingBatchAsync_WithCancellation_ThrowsOperationCancelledException()
    {
        // Arrange
        var texts = new List<string> { "test text" };
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _batchProcessingService.ProcessEmbeddingBatchAsync(texts, 1, cancellationTokenSource.Token);
        });
        
        Assert.NotNull(exception);
    }
}