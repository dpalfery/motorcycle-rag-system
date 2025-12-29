using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Application.Optimization;
using MotorcycleRAG.Contracts.Optimization;


namespace MotorcycleRAG.PerformanceTests;

/// <summary>
/// Performance benchmarks for batch processing functionality.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class BatchProcessingPerformanceTests
{
    private IBatchProcessingService _batchService = null!;
    private List<TestDocument> _testDocuments = null!;
    private BatchProcessingOptions _defaultOptions = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IBatchProcessingService, BatchProcessingService>();

        var serviceProvider = services.BuildServiceProvider();
        _batchService = serviceProvider.GetRequiredService<IBatchProcessingService>();

        // Create test documents
        _testDocuments = GenerateTestDocuments(1000);
        
        _defaultOptions = new BatchProcessingOptions
        {
            BatchSize = 100,
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            ProcessingTimeout = TimeSpan.FromMinutes(5),
            EnableRetry = true,
            MaxRetryAttempts = 2
        };
    }

    private List<TestDocument> GenerateTestDocuments(int count)
    {
        var documents = new List<TestDocument>();
        var random = new Random(42);
        
        for (int i = 0; i < count; i++)
        {
            documents.Add(new TestDocument
            {
                Id = $"doc-{i:D6}",
                Content = GenerateRandomContent(random, 100 + random.Next(900)), // 100-1000 chars
                ProcessingComplexity = random.Next(1, 6) // 1-5 complexity level
            });
        }
        
        return documents;
    }

    private string GenerateRandomContent(Random random, int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 ";
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    [Benchmark]
    [Arguments(100, 10)]
    [Arguments(500, 50)]
    [Arguments(1000, 100)]
    public async Task<BatchProcessingResult<ProcessedDocument>> BatchProcessing_Sequential(int documentCount, int batchSize)
    {
        var documents = _testDocuments.Take(documentCount);
        
        return await _batchService.ProcessBatchAsync(
            documents,
            ProcessDocumentsBatch,
            batchSize);
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(500)]
    [Arguments(1000)]
    public async Task<BatchProcessingResult<ProcessedDocument>> BatchProcessing_Parallel(int documentCount)
    {
        var documents = _testDocuments.Take(documentCount);
        var options = new BatchProcessingOptions
        {
            BatchSize = 100,
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };
        
        return await _batchService.ProcessParallelBatchAsync(
            documents,
            ProcessSingleDocument,
            options);
    }

    [Benchmark]
    [Arguments(2)]
    [Arguments(4)]
    [Arguments(8)]
    [Arguments(16)]
    public async Task<BatchProcessingResult<ProcessedDocument>> BatchProcessing_DifferentParallelism(int parallelism)
    {
        var documents = _testDocuments.Take(500);
        var options = new BatchProcessingOptions
        {
            BatchSize = 50,
            MaxDegreeOfParallelism = parallelism
        };
        
        return await _batchService.ProcessParallelBatchAsync(
            documents,
            ProcessSingleDocument,
            options);
    }

    private async Task<IEnumerable<ProcessedDocument>> ProcessDocumentsBatch(
        IEnumerable<TestDocument> documents, 
        CancellationToken cancellationToken)
    {
        var results = new List<ProcessedDocument>();
        
        foreach (var doc in documents)
        {
            // Simulate processing time based on complexity
            await Task.Delay(doc.ProcessingComplexity * 10, cancellationToken);
            
            results.Add(new ProcessedDocument
            {
                Id = doc.Id,
                OriginalContent = doc.Content,
                ProcessedContent = doc.Content.ToUpperInvariant(),
                ProcessingTime = TimeSpan.FromMilliseconds(doc.ProcessingComplexity * 10),
                WordCount = doc.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
            });
        }
        
        return results;
    }

    private async Task<ProcessedDocument> ProcessSingleDocument(
        TestDocument document, 
        CancellationToken cancellationToken)
    {
        // Simulate processing time based on complexity
        await Task.Delay(document.ProcessingComplexity * 10, cancellationToken);
        
        return new ProcessedDocument
        {
            Id = document.Id,
            OriginalContent = document.Content,
            ProcessedContent = document.Content.ToUpperInvariant(),
            ProcessingTime = TimeSpan.FromMilliseconds(document.ProcessingComplexity * 10),
            WordCount = document.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
        };
    }
}

/// <summary>
/// Unit tests for batch processing performance validation.
/// </summary>
public class BatchProcessingPerformanceValidationTests
{
    private readonly IBatchProcessingService _batchService;

    public BatchProcessingPerformanceValidationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IBatchProcessingService, BatchProcessingService>();

        var serviceProvider = services.BuildServiceProvider();
        _batchService = serviceProvider.GetRequiredService<IBatchProcessingService>();
    }

    [Fact]
    public async Task BatchProcessing_ShouldMeetThroughputTargets()
    {
        // Arrange
        var documents = GenerateTestDocuments(100);
        var targetThroughput = 50.0; // documents per second

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _batchService.ProcessBatchAsync(
            documents,
            ProcessDocumentsBatchFast,
            batchSize: 20);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.TotalProcessed.Should().Be(100);
        result.ThroughputPerSecond.Should().BeGreaterThan(targetThroughput, 
            $"Throughput should exceed {targetThroughput} documents/second");
        
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), 
            "Processing 100 documents should complete within 10 seconds");
    }

    [Fact]
    public async Task ParallelBatchProcessing_ShouldOutperformSequential()
    {
        // Arrange
        var documents = GenerateTestDocuments(200);
        var options = new BatchProcessingOptions
        {
            BatchSize = 50,
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        // Act - Sequential Processing
        var sequentialStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var sequentialResult = await _batchService.ProcessBatchAsync(
            documents,
            ProcessDocumentsBatchFast,
            batchSize: 50);
        sequentialStopwatch.Stop();

        // Act - Parallel Processing
        var parallelStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var parallelResult = await _batchService.ProcessParallelBatchAsync(
            documents,
            ProcessSingleDocumentFast,
            options);
        parallelStopwatch.Stop();

        // Assert
        sequentialResult.IsSuccess.Should().BeTrue();
        parallelResult.IsSuccess.Should().BeTrue();
        
        // Parallel should be faster for CPU-bound work with multiple cores
        if (Environment.ProcessorCount > 2)
        {
            parallelResult.ThroughputPerSecond.Should().BeGreaterThan(
                sequentialResult.ThroughputPerSecond * 1.2, 
                "Parallel processing should be at least 20% faster");
        }
    }

    [Fact]
    public async Task BatchSizeOptimization_ShouldImprovePerformance()
    {
        // Arrange
        var documents = GenerateTestDocuments(500);
        var documentSize = documents.Average(d => d.Content.Length);
        var availableMemory = 100 * 1024 * 1024; // 100MB

        // Act
        var optimalBatchSize = _batchService.OptimizeBatchSize(
            documents.Count, 
            (long)documentSize, 
            availableMemory);

        var result = await _batchService.ProcessBatchAsync(
            documents,
            ProcessDocumentsBatchFast,
            batchSize: optimalBatchSize);

        // Assert
        optimalBatchSize.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(documents.Count);
        result.IsSuccess.Should().BeTrue();
        result.ThroughputPerSecond.Should().BeGreaterThan(10, 
            "Optimized batch size should achieve reasonable throughput");
    }

    [Fact]
    public async Task BatchProcessingStatistics_ShouldTrackMetrics()
    {
        // Arrange
        var documents = GenerateTestDocuments(50);

        // Act
        var result = await _batchService.ProcessBatchAsync(
            documents,
            ProcessDocumentsBatchFast,
            batchSize: 10);

        var stats = _batchService.GetStatistics();

        // Assert
        stats.TotalBatchesProcessed.Should().BeGreaterThan(0);
        stats.TotalItemsProcessed.Should().BeGreaterThan(0);
        stats.AverageThroughputPerSecond.Should().BeGreaterThan(0);
        stats.SuccessRate.Should().BeGreaterThan(0.95); // At least 95% success rate
        stats.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task BatchProcessing_DifferentSizes_ShouldScale(int batchSize)
    {
        // Arrange
        var documents = GenerateTestDocuments(200);

        // Act
        var result = await _batchService.ProcessBatchAsync(
            documents,
            ProcessDocumentsBatchFast,
            batchSize: batchSize);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.TotalProcessed.Should().Be(200);
        result.ThroughputPerSecond.Should().BeGreaterThan(5, 
            $"Batch size {batchSize} should achieve minimum throughput");
    }

    [Fact]
    public async Task BatchProcessing_WithErrors_ShouldHandleGracefully()
    {
        // Arrange
        var documents = GenerateTestDocuments(50);
        
        // Act
        var result = await _batchService.ProcessBatchAsync(
            documents,
            ProcessDocumentsBatchWithErrors,
            batchSize: 10);

        // Assert
        result.TotalProcessed.Should().Be(50);
        result.Failed.Should().BeGreaterThan(0); // Some should fail
        result.SuccessfullyProcessed.Should().BeGreaterThan(0); // Some should succeed
        result.Errors.Should().NotBeEmpty();
        result.Errors.Should().AllSatisfy(error => 
        {
            error.Exception.Should().NotBeNull();
            error.ErrorMessage.Should().NotBeNullOrEmpty();
        });
    }

    private List<TestDocument> GenerateTestDocuments(int count)
    {
        var documents = new List<TestDocument>();
        var random = new Random(42);
        
        for (int i = 0; i < count; i++)
        {
            documents.Add(new TestDocument
            {
                Id = $"test-doc-{i:D6}",
                Content = $"Test document content {i} with some random data: {random.Next()}",
                ProcessingComplexity = random.Next(1, 4)
            });
        }
        
        return documents;
    }

    private async Task<IEnumerable<ProcessedDocument>> ProcessDocumentsBatchFast(
        IEnumerable<TestDocument> documents, 
        CancellationToken cancellationToken)
    {
        // Fast processing for performance tests
        await Task.Delay(10, cancellationToken); // Minimal delay
        
        return documents.Select(doc => new ProcessedDocument
        {
            Id = doc.Id,
            OriginalContent = doc.Content,
            ProcessedContent = doc.Content.ToUpperInvariant(),
            ProcessingTime = TimeSpan.FromMilliseconds(10),
            WordCount = doc.Content.Split(' ').Length
        });
    }

    private async Task<ProcessedDocument> ProcessSingleDocumentFast(
        TestDocument document, 
        CancellationToken cancellationToken)
    {
        // Fast processing for performance tests
        await Task.Delay(5, cancellationToken); // Minimal delay
        
        return new ProcessedDocument
        {
            Id = document.Id,
            OriginalContent = document.Content,
            ProcessedContent = document.Content.ToUpperInvariant(),
            ProcessingTime = TimeSpan.FromMilliseconds(5),
            WordCount = document.Content.Split(' ').Length
        };
    }

    private async Task<IEnumerable<ProcessedDocument>> ProcessDocumentsBatchWithErrors(
        IEnumerable<TestDocument> documents, 
        CancellationToken cancellationToken)
    {
        var results = new List<ProcessedDocument>();
        var random = new Random();
        
        foreach (var doc in documents)
        {
            // Randomly fail some documents (20% failure rate)
            if (random.NextDouble() < 0.2)
            {
                throw new InvalidOperationException($"Simulated processing error for document {doc.Id}");
            }
            
            await Task.Delay(5, cancellationToken);
            
            results.Add(new ProcessedDocument
            {
                Id = doc.Id,
                OriginalContent = doc.Content,
                ProcessedContent = doc.Content.ToUpperInvariant(),
                ProcessingTime = TimeSpan.FromMilliseconds(5),
                WordCount = doc.Content.Split(' ').Length
            });
        }
        
        return results;
    }
}

/// <summary>
/// Test document model for batch processing tests.
/// </summary>
public class TestDocument
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int ProcessingComplexity { get; set; } = 1;
}

/// <summary>
/// Processed document model for batch processing tests.
/// </summary>
public class ProcessedDocument
{
    public string Id { get; set; } = string.Empty;
    public string OriginalContent { get; set; } = string.Empty;
    public string ProcessedContent { get; set; } = string.Empty;
    public TimeSpan ProcessingTime { get; set; }
    public int WordCount { get; set; }
}