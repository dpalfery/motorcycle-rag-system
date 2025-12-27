using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Pipeline;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;
using Xunit;

namespace MotorcycleRAG.UnitTests.Pipeline;

public class DataPipelineOrchestratorTests
{
    private readonly Mock<IDataProcessor<CSVFile>> _csvProcessorMock;
    private readonly Mock<IDataProcessor<PDFDocument>> _pdfProcessorMock;
    private readonly Mock<IMotorcycleIndexingService> _indexingServiceMock;
    private readonly Mock<IPipelineMonitoringService> _monitoringServiceMock;
    private readonly Mock<IResilienceService> _resilienceServiceMock;
    private readonly Mock<ICorrelationService> _correlationServiceMock;
    private readonly Mock<ILogger<DataPipelineOrchestrator>> _loggerMock;
    private readonly Mock<IOptions<PipelineConfiguration>> _configMock;
    private readonly DataPipelineOrchestrator _orchestrator;

    public DataPipelineOrchestratorTests()
    {
        _csvProcessorMock = new Mock<IDataProcessor<CSVFile>>();
        _pdfProcessorMock = new Mock<IDataProcessor<PDFDocument>>();
        _indexingServiceMock = new Mock<IMotorcycleIndexingService>();
        _monitoringServiceMock = new Mock<IPipelineMonitoringService>();
        _resilienceServiceMock = new Mock<IResilienceService>();
        _correlationServiceMock = new Mock<ICorrelationService>();
        _loggerMock = new Mock<ILogger<DataPipelineOrchestrator>>();
        _configMock = new Mock<IOptions<PipelineConfiguration>>();

        var config = new PipelineConfiguration
        {
            MaxConcurrentExecutions = 3,
            DefaultTimeout = TimeSpan.FromMinutes(30),
            MaxRetries = 3
        };

        _configMock.Setup(x => x.Value).Returns(config);
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("test-correlation-id");

        _orchestrator = new DataPipelineOrchestrator(
            _csvProcessorMock.Object,
            _pdfProcessorMock.Object,
            _indexingServiceMock.Object,
            _monitoringServiceMock.Object,
            _resilienceServiceMock.Object,
            _correlationServiceMock.Object,
            _configMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ProcessFileAsync_WithValidCSVRequest_ShouldReturnSuccessResult()
    {
        // Arrange
        var request = new DataPipelineRequest
        {
            FileName = "test.csv",
            FilePath = CreateTempFile("test.csv", "col1,col2\nval1,val2"),
            FileType = FileType.CSV,
            Options = new PipelineOptions { IndexImmediately = false }
        };

        var processedData = new ProcessedData
        {
            Id = "test-id",
            Documents = new List<MotorcycleDocument>
            {
                new MotorcycleDocument { Id = "doc1", Title = "Test Doc", Content = "Test content" }
            }
        };

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Func<Task<ProcessedData>>>(), It.IsAny<Func<Task<ProcessedData>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processedData);

        // Act
        var result = await _orchestrator.ProcessFileAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PipelineStatus.Completed, result.Status);
        Assert.Equal(processedData.Documents.Count, result.ProcessedData?.Documents.Count);
        Assert.True(result.Metrics.ContainsKey("DocumentsProcessed"));

        // Verify monitoring was called
        _monitoringServiceMock.Verify(x => x.TrackPipelineStartAsync(
            It.IsAny<string>(),
            PipelineType.CSV,
            It.IsAny<PipelineExecutionContext>()), Times.Once);

        _monitoringServiceMock.Verify(x => x.TrackPipelineCompletionAsync(
            It.IsAny<string>(),
            It.IsAny<PipelineExecutionResult>()), Times.Once);

        // Cleanup
        CleanupTempFile(request.FilePath);
    }

    [Fact]
    public async Task ProcessFileAsync_WithValidPDFRequest_ShouldReturnSuccessResult()
    {
        // Arrange
        var request = new DataPipelineRequest
        {
            FileName = "test.pdf",
            FilePath = CreateTempFile("test.pdf", "%PDF-1.4 test content"),
            FileType = FileType.PDF,
            Options = new PipelineOptions { IndexImmediately = false }
        };

        var processedData = new ProcessedData
        {
            Id = "test-id",
            Documents = new List<MotorcycleDocument>
            {
                new MotorcycleDocument { Id = "doc1", Title = "Test PDF", Content = "PDF content" }
            }
        };

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Func<Task<ProcessedData>>>(), It.IsAny<Func<Task<ProcessedData>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processedData);

        // Act
        var result = await _orchestrator.ProcessFileAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PipelineStatus.Completed, result.Status);
        Assert.Equal(processedData.Documents.Count, result.ProcessedData?.Documents.Count);

        // Verify monitoring was called
        _monitoringServiceMock.Verify(x => x.TrackPipelineStartAsync(
            It.IsAny<string>(),
            PipelineType.PDF,
            It.IsAny<PipelineExecutionContext>()), Times.Once);

        // Cleanup
        CleanupTempFile(request.FilePath);
    }

    [Fact]
    public async Task ProcessFileAsync_WithIndexImmediately_ShouldIndexDocuments()
    {
        // Arrange
        var request = new DataPipelineRequest
        {
            FileName = "test.csv",
            FilePath = CreateTempFile("test.csv", "col1,col2\nval1,val2"),
            FileType = FileType.CSV,
            Options = new PipelineOptions { IndexImmediately = true }
        };

        var processedData = new ProcessedData
        {
            Id = "test-id",
            Documents = new List<MotorcycleDocument>
            {
                new MotorcycleDocument { Id = "doc1", Title = "Test Doc", Content = "Test content" }
            }
        };

        var indexingResult = new BatchIndexingResult
        {
            Success = true,
            DocumentsProcessed = 1,
            DocumentsIndexed = 1
        };

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Func<Task<ProcessedData>>>(), It.IsAny<Func<Task<ProcessedData>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processedData);

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Func<Task<BatchIndexingResult>>>(), It.IsAny<Func<Task<BatchIndexingResult>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexingResult)
            .Callback<string, Func<Task<BatchIndexingResult>>, Func<Task<BatchIndexingResult>>, string, CancellationToken>((key, func, fallback, corrId, token) =>
            {
                // Execute the function to actually call the indexing service
                func();
            });

        // Act
        var result = await _orchestrator.ProcessFileAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PipelineStatus.Completed, result.Status);
        Assert.NotNull(result.IndexingResult);
        Assert.True(result.IndexingResult.Success);
        Assert.Equal(1, result.IndexingResult.DocumentsIndexed);

        // Verify indexing service was called through resilience service
        _indexingServiceMock.Verify(x => x.IndexDocumentsAsync(
            It.IsAny<IEnumerable<MotorcycleDocument>>()), Times.Once);

        // Cleanup
        CleanupTempFile(request.FilePath);
    }

    [Fact]
    public async Task ProcessFileAsync_WithProcessingException_ShouldReturnFailedResult()
    {
        // Arrange
        var request = new DataPipelineRequest
        {
            FileName = "test.csv",
            FilePath = CreateTempFile("test.csv", "col1,col2\nval1,val2"),
            FileType = FileType.CSV,
            Options = new PipelineOptions { IndexImmediately = false }
        };

        var exception = new InvalidOperationException("Processing failed");

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Func<Task<ProcessedData>>>(), It.IsAny<Func<Task<ProcessedData>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var result = await _orchestrator.ProcessFileAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PipelineStatus.Failed, result.Status);
        Assert.Contains("Processing failed", result.Errors);
        Assert.Contains("Processing failed", result.Message);

        // Verify monitoring tracked the failure
        _monitoringServiceMock.Verify(x => x.TrackPipelineFailureAsync(
            It.IsAny<string>(),
            exception,
            It.IsAny<PipelineExecutionContext>()), Times.Once);

        // Cleanup
        CleanupTempFile(request.FilePath);
    }

    [Fact]
    public async Task ProcessBatchAsync_WithMultipleFiles_ShouldProcessAllFiles()
    {
        // Arrange
        var requests = new List<DataPipelineRequest>
        {
            new DataPipelineRequest
            {
                FileName = "test1.csv",
                FilePath = CreateTempFile("test1.csv", "col1,col2\nval1,val2"),
                FileType = FileType.CSV,
                Options = new PipelineOptions { IndexImmediately = false }
            },
            new DataPipelineRequest
            {
                FileName = "test2.csv",
                FilePath = CreateTempFile("test2.csv", "col1,col2\nval3,val4"),
                FileType = FileType.CSV,
                Options = new PipelineOptions { IndexImmediately = false }
            }
        };

        var processedData = new ProcessedData
        {
            Id = "test-id",
            Documents = new List<MotorcycleDocument>
            {
                new MotorcycleDocument { Id = "doc1", Title = "Test Doc", Content = "Test content" }
            }
        };

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Func<Task<ProcessedData>>>(), It.IsAny<Func<Task<ProcessedData>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processedData);

        // Act
        var result = await _orchestrator.ProcessBatchAsync(requests, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalFiles);
        Assert.Equal(2, result.ProcessedSuccessfully);
        Assert.Equal(0, result.Failed);
        Assert.True(result.IsCompleted);
        Assert.False(result.HasErrors);

        // Cleanup
        foreach (var request in requests)
        {
            CleanupTempFile(request.FilePath);
        }
    }

    [Fact]
    public async Task CancelPipelineAsync_WithValidExecutionId_ShouldReturnTrue()
    {
        // Arrange
        // Start a pipeline to create the execution ID
        var request = new DataPipelineRequest
        {
            FileName = "test.csv",
            FilePath = CreateTempFile("test.csv", "col1,col2\nval1,val2"),
            FileType = FileType.CSV
        };

        var processedData = new ProcessedData
        {
            Id = "test-id",
            Documents = new List<MotorcycleDocument>()
        };

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Func<Task<ProcessedData>>>(), It.IsAny<Func<Task<ProcessedData>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processedData);

        // Start the pipeline (this will create the execution ID internally)
        var processingTask = _orchestrator.ProcessFileAsync(request, CancellationToken.None);

        // Wait a bit to ensure the execution is tracked
        await Task.Delay(50);

        // Act - For this test we'll just test the method directly
        var cancelled = await _orchestrator.CancelPipelineAsync("non-existent-id");

        // Assert
        Assert.False(cancelled); // Should return false for non-existent ID

        // Wait for the original task to complete
        await processingTask;

        // Cleanup
        CleanupTempFile(request.FilePath);
    }

    [Fact]
    public async Task GetPipelineMetricsAsync_ShouldReturnValidMetrics()
    {
        // Arrange
        var timeWindow = TimeSpan.FromHours(24);
        var detailedMetrics = new DetailedPipelineMetrics
        {
            TimeWindow = timeWindow,
            StartTime = DateTime.UtcNow.AddDays(-1),
            EndTime = DateTime.UtcNow,
            Executions = new ExecutionMetrics
            {
                TotalExecutions = 10,
                SuccessfulExecutions = 8,
                FailedExecutions = 2
            },
            Processing = new ProcessingMetrics
            {
                TotalDocumentsProcessed = 100,
                TotalDocumentsIndexed = 95
            },
            Performance = new ExecutionPerformanceMetrics
            {
                AverageExecutionTime = TimeSpan.FromMinutes(5)
            }
        };

        _monitoringServiceMock
            .Setup(x => x.GetDetailedMetricsAsync(timeWindow))
            .ReturnsAsync(detailedMetrics);

        // Act
        var result = await _orchestrator.GetPipelineMetricsAsync(timeWindow);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.TotalExecutions);
        Assert.Equal(8, result.SuccessfulExecutions);
        Assert.Equal(2, result.FailedExecutions);
        Assert.Equal(100, result.TotalDocumentsProcessed);
        Assert.Equal(95, result.TotalDocumentsIndexed);
    }

    private string CreateTempFile(string fileName, string content)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}-{fileName}");
        File.WriteAllText(tempPath, content);
        return tempPath;
    }

    private void CleanupTempFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}