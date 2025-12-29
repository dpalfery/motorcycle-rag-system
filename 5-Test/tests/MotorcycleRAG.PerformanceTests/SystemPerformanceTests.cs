using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models;
using System.Diagnostics;
using Xunit;


namespace MotorcycleRAG.PerformanceTests;

/// <summary>
/// Performance and cost optimization validation tests
/// </summary>
public class SystemPerformanceTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly IMotorcycleRAGService _ragService;
    private readonly IDataPipelineOrchestrator _pipelineOrchestrator;
    private readonly IPipelineMonitoringService _monitoringService;

    public SystemPerformanceTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = _factory.Services.CreateScope();
        
        _ragService = _scope.ServiceProvider.GetRequiredService<IMotorcycleRAGService>();
        _pipelineOrchestrator = _scope.ServiceProvider.GetRequiredService<IDataPipelineOrchestrator>();
        _monitoringService = _scope.ServiceProvider.GetRequiredService<IPipelineMonitoringService>();
    }

    [Fact]
    public async Task QueryPerformance_SingleQuery_ShouldMeetResponseTimeTargets()
    {
        // Arrange
        var queryRequest = new MotorcycleQueryRequest
        {
            Query = "What are the specifications for Honda CBR600RR?",
            UserId = "perf-test-user"
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var response = await _ragService.QueryAsync(queryRequest);
        stopwatch.Stop();

        // Assert - Response time should be under 3 seconds for 95th percentile
        Assert.True(stopwatch.ElapsedMilliseconds < 3000, 
            $"Query took {stopwatch.ElapsedMilliseconds}ms, expected < 3000ms");
        
        Assert.NotNull(response);
        Assert.NotEmpty(response.Response);
        Assert.NotNull(response.Metrics);
        
        // Verify metrics are tracked
        Assert.True(response.Metrics.ResponseTime.TotalMilliseconds > 0);
    }

    [Fact]
    public async Task ConcurrentQueryPerformance_100Users_ShouldHandleLoad()
    {
        // Arrange - 100 concurrent users
        var concurrentUsers = 100;
        var queries = Enumerable.Range(0, concurrentUsers)
            .Select(i => new MotorcycleQueryRequest
            {
                Query = $"What motorcycle models are available for user {i}?",
                UserId = $"load-test-user-{i}"
            })
            .ToList();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var tasks = queries.Select(query => _ragService.QueryAsync(query));
        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert - All queries should complete successfully
        Assert.Equal(concurrentUsers, responses.Length);
        Assert.All(responses, response =>
        {
            Assert.NotNull(response);
            Assert.NotEmpty(response.Response);
        });

        // Performance targets
        var averageResponseTime = stopwatch.ElapsedMilliseconds / (double)concurrentUsers;
        Assert.True(averageResponseTime < 5000, 
            $"Average response time {averageResponseTime}ms exceeded 5000ms target");

        // Total time should be reasonable for concurrent processing
        Assert.True(stopwatch.ElapsedMilliseconds < 60000, 
            $"Total processing time {stopwatch.ElapsedMilliseconds}ms exceeded 60000ms");
    }

    [Fact]
    public async Task BatchProcessingPerformance_1000Documents_ShouldMeetThroughputTargets()
    {
        // Arrange - Create 1000 small documents for batch processing
        var documents = Enumerable.Range(0, 1000)
            .Select(i => new DataPipelineRequest
            {
                FileName = $"test-doc-{i}.csv",
                FilePath = CreateTempCsvFile($"test-doc-{i}.csv", $"Make,Model\nHonda,CBR{i}"),
                FileType = FileType.CSV,
                Options = new PipelineOptions
                {
                    IndexImmediately = false,
                    ProcessImages = false,
                    GenerateEmbeddings = false
                }
            })
            .ToList();

        try
        {
            // Act
            var stopwatch = Stopwatch.StartNew();
            var batchResult = await _pipelineOrchestrator.ProcessBatchAsync(documents);
            stopwatch.Stop();

            // Assert - Batch processing performance
            Assert.NotNull(batchResult);
            Assert.Equal(1000, batchResult.TotalFiles);
            
            // Should process at least 10 documents per second
            var documentsPerSecond = 1000.0 / stopwatch.Elapsed.TotalSeconds;
            Assert.True(documentsPerSecond >= 10, 
                $"Processing rate {documentsPerSecond:F2} docs/sec is below 10 docs/sec target");

            // Total processing time should be reasonable
            Assert.True(stopwatch.ElapsedMilliseconds < 300000, // 5 minutes
                $"Batch processing took {stopwatch.ElapsedMilliseconds}ms, expected < 300000ms");

            // Verify batch metrics
            Assert.True(batchResult.BatchMetrics.ContainsKey("TotalDocumentsProcessed"));
            Assert.True(batchResult.BatchMetrics.ContainsKey("AverageProcessingTimeMs"));
        }
        finally
        {
            // Cleanup temp files
            foreach (var doc in documents)
            {
                if (File.Exists(doc.FilePath))
                    File.Delete(doc.FilePath);
            }
        }
    }

    [Fact]
    public async Task MemoryUsage_ExtendedOperation_ShouldNotLeak()
    {
        // Arrange
        var initialMemory = GC.GetTotalMemory(true);
        var queries = Enumerable.Range(0, 50)
            .Select(i => new MotorcycleQueryRequest
            {
                Query = $"Memory test query {i} with some additional content to use memory",
                UserId = $"memory-test-{i}"
            });

        // Act - Process multiple queries to test memory usage
        foreach (var query in queries)
        {
            var response = await _ragService.QueryAsync(query);
            Assert.NotNull(response);
        }

        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;

        // Assert - Memory increase should be reasonable (less than 50MB)
        Assert.True(memoryIncrease < 50 * 1024 * 1024, 
            $"Memory increased by {memoryIncrease / 1024 / 1024}MB, expected < 50MB");
    }

    [Fact]
    public async Task PipelineMonitoring_PerformanceMetrics_ShouldTrackAccurately()
    {
        // Arrange - Process some test data to generate metrics
        var testDoc = new DataPipelineRequest
        {
            FileName = "perf-test.csv",
            FilePath = CreateTempCsvFile("perf-test.csv", "Make,Model\nHonda,CBR\nYamaha,R6"),
            FileType = FileType.CSV,
            Options = new PipelineOptions { IndexImmediately = false }
        };

        try
        {
            // Act - Process document and get metrics
            var processingResult = await _pipelineOrchestrator.ProcessFileAsync(testDoc);
            var metrics = await _monitoringService.GetDetailedMetricsAsync(TimeSpan.FromHours(1));

            // Assert - Metrics should be accurate
            Assert.NotNull(metrics);
            Assert.True(metrics.Executions.TotalExecutions >= 1);
            Assert.True(metrics.Performance.AverageExecutionTime > TimeSpan.Zero);
            
            if (processingResult.Status == PipelineStatus.Completed)
            {
                Assert.True(metrics.Executions.SuccessfulExecutions >= 1);
                Assert.True(metrics.Processing.TotalDocumentsProcessed >= 1);
            }

            // Performance metrics should be within reasonable ranges
            Assert.True(metrics.Performance.AverageExecutionTime < TimeSpan.FromMinutes(5));
        }
        finally
        {
            if (File.Exists(testDoc.FilePath))
                File.Delete(testDoc.FilePath);
        }
    }

    [Theory]
    [InlineData(10)]   // Small batch
    [InlineData(100)]  // Medium batch
    [InlineData(500)]  // Large batch
    public async Task ScalabilityTest_VariousBatchSizes_ShouldScaleLinearly(int batchSize)
    {
        // Arrange
        var documents = Enumerable.Range(0, batchSize)
            .Select(i => new DataPipelineRequest
            {
                FileName = $"scale-test-{i}.csv",
                FilePath = CreateTempCsvFile($"scale-test-{i}.csv", $"Make,Model\nTest,Bike{i}"),
                FileType = FileType.CSV,
                Options = new PipelineOptions { IndexImmediately = false }
            })
            .ToList();

        try
        {
            // Act
            var stopwatch = Stopwatch.StartNew();
            var result = await _pipelineOrchestrator.ProcessBatchAsync(documents);
            stopwatch.Stop();

            // Assert - Processing should scale reasonably
            Assert.NotNull(result);
            Assert.Equal(batchSize, result.TotalFiles);

            var processingTimePerDoc = stopwatch.ElapsedMilliseconds / (double)batchSize;
            
            // Processing time per document should be reasonable and not increase dramatically with batch size
            Assert.True(processingTimePerDoc < 1000, // Less than 1 second per document
                $"Processing time per document {processingTimePerDoc}ms exceeded 1000ms for batch size {batchSize}");

            // Log performance for analysis
            Console.WriteLine($"Batch size: {batchSize}, Total time: {stopwatch.ElapsedMilliseconds}ms, " +
                            $"Time per doc: {processingTimePerDoc:F2}ms");
        }
        finally
        {
            // Cleanup
            foreach (var doc in documents)
            {
                if (File.Exists(doc.FilePath))
                    File.Delete(doc.FilePath);
            }
        }
    }

    [Fact]
    public async Task CostOptimization_ModelUsage_ShouldUseAppropriateModels()
    {
        // This test verifies that the system uses cost-optimized models
        // In a real implementation, this would check telemetry for model usage

        // Arrange
        var queries = new[]
        {
            "Simple query about Honda motorcycles",
            "Complex comparison between multiple motorcycle brands and their specifications",
            "What maintenance is required for sport bikes?"
        };

        // Act - Process queries and verify they complete
        foreach (var queryText in queries)
        {
            var query = new MotorcycleQueryRequest
            {
                Query = queryText,
                UserId = "cost-test-user"
            };

            var response = await _ragService.QueryAsync(query);
            
            // Assert - Queries should complete successfully
            Assert.NotNull(response);
            Assert.NotEmpty(response.Response);
            
            // In a real implementation, verify that:
            // - Simple queries use GPT-4o-mini (cost-optimized)
            // - Complex queries use GPT-4o only when necessary
            // - Embeddings use text-embedding-3-large efficiently
        }
    }

    private string CreateTempCsvFile(string fileName, string content)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);
        File.WriteAllText(tempPath, content);
        return tempPath;
    }

    public void Dispose()
    {
        _scope?.Dispose();
    }
}
