using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Pipeline;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models;
using Xunit;

namespace MotorcycleRAG.UnitTests.Pipeline;

/// <summary>
/// Reliability tests for ScheduledPipelineService to ensure robust scheduled processing
/// </summary>
public class ScheduledPipelineServiceReliabilityTests : IDisposable
{
    private readonly Mock<IServiceScopeFactory> _serviceScopeFactoryMock;
    private readonly Mock<IServiceScope> _serviceScopeMock;
    private readonly Mock<IDataPipelineOrchestrator> _orchestratorMock;
    private readonly Mock<ILogger<ScheduledPipelineService>> _loggerMock;
    private readonly Mock<IOptions<ScheduledProcessingConfiguration>> _configMock;
    private readonly Mock<IIngestionJobRepository> _ingestionJobRepositoryMock;
    private readonly ScheduledPipelineService _service;
    private readonly string _testDirectory;
    private readonly IServiceProvider _serviceProvider;

    public ScheduledPipelineServiceReliabilityTests()
    {
        _serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();
        _serviceScopeMock = new Mock<IServiceScope>();
        _orchestratorMock = new Mock<IDataPipelineOrchestrator>();
        _loggerMock = new Mock<ILogger<ScheduledPipelineService>>();
        _configMock = new Mock<IOptions<ScheduledProcessingConfiguration>>();
        _ingestionJobRepositoryMock = new Mock<IIngestionJobRepository>();

        // Create a temporary directory for testing
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ScheduledPipelineTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);

        var config = new ScheduledProcessingConfiguration
        {
            DefaultCronExpression = "0 2 * * *", // Daily at 2 AM
            IsEnabledByDefault = true,
            DefaultProcessingWindow = TimeSpan.FromHours(4),
            DefaultMaxConcurrentJobs = 3,
            BaseDirectory = _testDirectory
        };

        _configMock.Setup(x => x.Value).Returns(config);

        // Setup service scope factory
        _serviceScopeFactoryMock.Setup(x => x.CreateScope()).Returns(_serviceScopeMock.Object);
        
        // Create a real service provider with the orchestrator mock
        var services = new ServiceCollection();
        services.AddSingleton(_orchestratorMock.Object);
        _serviceProvider = services.BuildServiceProvider();
        
        _serviceScopeMock.Setup(x => x.ServiceProvider).Returns(_serviceProvider);

        _service = new ScheduledPipelineService(
            _serviceScopeFactoryMock.Object,
            _ingestionJobRepositoryMock.Object,
            _configMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task StartAsync_WithValidConfiguration_ShouldInitializeSuccessfully()
    {
        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        var nextExecution = await _service.GetNextExecutionTimeAsync();
        Assert.NotNull(nextExecution);
        Assert.True(nextExecution > DateTime.UtcNow);

        // Cleanup
        await _service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteImmediateRunAsync_WithNoFiles_ShouldCompleteSuccessfully()
    {
        // Arrange
        var batchResult = new BatchPipelineResult
        {
            TotalFiles = 0,
            ProcessedSuccessfully = 0,
            Failed = 0,
            EndTime = DateTime.UtcNow
        };

        _orchestratorMock.Setup(x => x.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        // Act
        var result = await _service.ExecuteImmediateRunAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PipelineStatus.Completed, result.Status);
        Assert.Contains("No files found to process", result.Message);
    }

    [Fact]
    public async Task ExecuteImmediateRunAsync_WithValidFiles_ShouldProcessSuccessfully()
    {
        // Arrange
        var scheduledDir = Path.Combine(_testDirectory, "scheduled");
        Directory.CreateDirectory(scheduledDir);

        // Create test files
        var csvFile = Path.Combine(scheduledDir, "test.csv");
        var pdfFile = Path.Combine(scheduledDir, "manual.pdf");
        File.WriteAllText(csvFile, "Make,Model\nHonda,CBR");
        File.WriteAllText(pdfFile, "%PDF-1.4 test content");

        var batchResult = new BatchPipelineResult
        {
            TotalFiles = 2,
            ProcessedSuccessfully = 2,
            Failed = 0,
            EndTime = DateTime.UtcNow
        };

        _orchestratorMock.Setup(x => x.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        // Act
        var result = await _service.ExecuteImmediateRunAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PipelineStatus.Completed, result.Status);
        Assert.Contains("Processed 2 files successfully", result.Message);

        // Verify orchestrator was called with correct requests
        _orchestratorMock.Verify(x => x.ProcessBatchAsync(
            It.Is<IEnumerable<DataPipelineRequest>>(requests => 
                requests.Count() == 2 &&
                requests.Any(r => r.FileType == FileType.CSV) &&
                requests.Any(r => r.FileType == FileType.PDF)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteImmediateRunAsync_WithProcessingErrors_ShouldHandleGracefully()
    {
        // Arrange
        var scheduledDir = Path.Combine(_testDirectory, "scheduled");
        Directory.CreateDirectory(scheduledDir);
        File.WriteAllText(Path.Combine(scheduledDir, "test.csv"), "Make,Model\nHonda,CBR");

        var batchResult = new BatchPipelineResult
        {
            TotalFiles = 1,
            ProcessedSuccessfully = 0,
            Failed = 1,
            EndTime = DateTime.UtcNow
        };

        _orchestratorMock.Setup(x => x.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        // Act
        var result = await _service.ExecuteImmediateRunAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PipelineStatus.PartiallyCompleted, result.Status);
        Assert.Contains("0 files successfully, 1 failed", result.Message);
    }

    [Fact]
    public async Task ExecuteImmediateRunAsync_WithException_ShouldReturnFailedResult()
    {
        // Arrange
        var scheduledDir = Path.Combine(_testDirectory, "scheduled");
        Directory.CreateDirectory(scheduledDir);
        File.WriteAllText(Path.Combine(scheduledDir, "test.csv"), "Make,Model\nHonda,CBR");

        _orchestratorMock.Setup(x => x.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Processing failed"));

        // Act
        var result = await _service.ExecuteImmediateRunAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PipelineStatus.Failed, result.Status);
        Assert.Contains("Processing failed", result.Message);
        Assert.Contains("Processing failed", result.Errors);
    }

    [Fact]
    public async Task GetProcessingStatsAsync_ShouldReturnValidStats()
    {
        // Act
        var stats = await _service.GetProcessingStatsAsync();

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(0, stats.TotalScheduledRuns);
        Assert.Equal(0, stats.SuccessfulRuns);
        Assert.Equal(0, stats.FailedRuns);
        Assert.Equal(TimeSpan.Zero, stats.AverageProcessingTime);
    }

    [Fact]
    public async Task UpdateScheduleAsync_WithValidCronExpression_ShouldUpdateSuccessfully()
    {
        // Arrange
        var newConfig = new ProcessingScheduleConfig
        {
            CronExpression = "0 4 * * *", // Daily at 4 AM
            IsEnabled = true,
            ProcessingWindow = TimeSpan.FromHours(2),
            MaxConcurrentJobs = 5,
            ProcessingDirectory = "custom-scheduled"
        };

        // Act
        await _service.UpdateScheduleAsync(newConfig);

        // Assert
        var nextExecution = await _service.GetNextExecutionTimeAsync();
        Assert.NotNull(nextExecution);
        
        // The next execution should be at 4 AM (not 2 AM as in original config)
        Assert.Equal(4, nextExecution.Value.Hour);
    }

    [Fact]
    public async Task UpdateScheduleAsync_WithInvalidCronExpression_ShouldHandleGracefully()
    {
        // Arrange
        var invalidConfig = new ProcessingScheduleConfig
        {
            CronExpression = "invalid cron expression",
            IsEnabled = true
        };

        // Act
        await _service.UpdateScheduleAsync(invalidConfig);

        // Assert
        var nextExecution = await _service.GetNextExecutionTimeAsync();
        Assert.Null(nextExecution); // Should be null due to invalid cron expression
    }

    [Fact]
    public async Task UpdateScheduleAsync_WithDisabledSchedule_ShouldDisableExecution()
    {
        // Arrange
        var disabledConfig = new ProcessingScheduleConfig
        {
            CronExpression = "0 0 2 * * *",
            IsEnabled = false
        };

        // Act
        await _service.UpdateScheduleAsync(disabledConfig);

        // Assert
        var nextExecution = await _service.GetNextExecutionTimeAsync();
        Assert.Null(nextExecution); // Should be null when disabled
    }

    [Theory]
    [InlineData("test.csv", FileType.CSV)]
    [InlineData("manual.pdf", FileType.PDF)]
    [InlineData("unknown.txt", FileType.Unknown)]
    public async Task ExecuteImmediateRunAsync_ShouldDetectCorrectFileTypes(string fileName, FileType expectedType)
    {
        // Arrange
        var scheduledDir = Path.Combine(_testDirectory, "scheduled");
        Directory.CreateDirectory(scheduledDir);

        var content = fileName.EndsWith(".pdf") ? "%PDF-1.4 content" : "test,content";
        File.WriteAllText(Path.Combine(scheduledDir, fileName), content);

        var batchResult = new BatchPipelineResult
        {
            TotalFiles = expectedType == FileType.Unknown ? 0 : 1,
            ProcessedSuccessfully = expectedType == FileType.Unknown ? 0 : 1,
            Failed = 0,
            EndTime = DateTime.UtcNow
        };

        _orchestratorMock.Setup(x => x.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        // Act
        var result = await _service.ExecuteImmediateRunAsync(CancellationToken.None);

        // Assert
        if (expectedType == FileType.Unknown)
        {
            // Unknown file types should be ignored
            Assert.Contains("No files found to process", result.Message);
        }
        else
        {
            // Known file types should be processed
            _orchestratorMock.Verify(x => x.ProcessBatchAsync(
                It.Is<IEnumerable<DataPipelineRequest>>(requests => 
                    requests.Any(r => r.FileType == expectedType)),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task ExecuteImmediateRunAsync_WithCancellation_ShouldHandleCancellationGracefully()
    {
        // Arrange
        var scheduledDir = Path.Combine(_testDirectory, "scheduled");
        Directory.CreateDirectory(scheduledDir);
        File.WriteAllText(Path.Combine(scheduledDir, "test.csv"), "Make,Model\nHonda,CBR");

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await _service.ExecuteImmediateRunAsync(cancellationTokenSource.Token);
        });
    }

    [Fact]
    public async Task StatsTracking_ShouldUpdateCorrectlyAfterExecution()
    {
        // Arrange
        var scheduledDir = Path.Combine(_testDirectory, "scheduled");
        Directory.CreateDirectory(scheduledDir);
        File.WriteAllText(Path.Combine(scheduledDir, "test.csv"), "Make,Model\nHonda,CBR");

        _orchestratorMock.Setup(x => x.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new BatchPipelineResult
            {
                TotalFiles = 1,
                ProcessedSuccessfully = 1,
                Failed = 0,
                EndTime = DateTime.UtcNow.AddMilliseconds(100)
            });

        // Act
        await _service.ExecuteImmediateRunAsync(CancellationToken.None);

        // Assert
        var stats = await _service.GetProcessingStatsAsync();
        Assert.Equal(1, stats.TotalScheduledRuns);
        Assert.Equal(1, stats.SuccessfulRuns);
        Assert.Equal(0, stats.FailedRuns);
        Assert.Equal(1, stats.FilesProcessedInLastRun);
        Assert.Equal(PipelineStatus.Completed, stats.LastExecutionStatus);
        Assert.True(stats.AverageProcessingTime > TimeSpan.Zero);
    }

    public void Dispose()
    {
        // Cleanup test directory
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }

        _service?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }
}
