using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using Xunit;


namespace MotorcycleRAG.UnitTests.Pipeline;

/// <summary>
/// Reliability tests for ScheduledPipelineService to ensure robust scheduled processing
/// </summary>
public class ScheduledPipelineServiceReliabilityTests : IDisposable {
    private readonly Mock<IServiceScopeFactory> _serviceScopeFactoryMock;
    private readonly Mock<IServiceScope> _serviceScopeMock;
    private readonly Mock<IDataPipelineOrchestrator> _orchestratorMock;
    private readonly Mock<ILogger<ScheduledPipelineService>> _loggerMock;
    private readonly Mock<IOptions<ScheduledProcessingConfiguration>> _configMock;
    private readonly Mock<IOptions<AzureFoundryOptions>> _azureFoundryOptionsMock;
    private readonly Mock<ILocalFileStore> _localFileStoreMock;
    private readonly Mock<ILocalFileDiscovery> _localFileDiscoveryMock;
    private readonly ScheduledPipelineService _service;
    private readonly IServiceProvider _serviceProvider;
    private const string ScheduledDirectory = "/scheduled-root/scheduled";

    public ScheduledPipelineServiceReliabilityTests() {
        _serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();
        _serviceScopeMock = new Mock<IServiceScope>();
        _orchestratorMock = new Mock<IDataPipelineOrchestrator>();
        _loggerMock = new Mock<ILogger<ScheduledPipelineService>>();
        _configMock = new Mock<IOptions<ScheduledProcessingConfiguration>>();
        _azureFoundryOptionsMock = new Mock<IOptions<AzureFoundryOptions>>();
        _localFileStoreMock = new Mock<ILocalFileStore>();
        _localFileDiscoveryMock = new Mock<ILocalFileDiscovery>();

        var config = new ScheduledProcessingConfiguration {
            DefaultCronExpression = "0 2 * * *", // Daily at 2 AM
            IsEnabledByDefault = true,
            DefaultProcessingWindow = TimeSpan.FromHours(4),
            DefaultMaxConcurrentJobs = 3,
            BaseDirectory = "/scheduled-root"
        };

        _configMock.Setup(x => x.Value).Returns(config);
        _azureFoundryOptionsMock.Setup(x => x.Value).Returns(new AzureFoundryOptions {
            DocumentIntelligenceEndpoint = string.Empty
        });
        _localFileStoreMock
            .Setup(x => x.EnsureDirectoryExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _localFileDiscoveryMock
            .Setup(x => x.GetTopLevelFilePathsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        // Setup service scope factory
        _serviceScopeFactoryMock.Setup(x => x.CreateScope()).Returns(_serviceScopeMock.Object);

        // Create a real service provider with the orchestrator mock
        var services = new ServiceCollection();
        services.AddSingleton(_orchestratorMock.Object);
        _serviceProvider = services.BuildServiceProvider();

        _serviceScopeMock.Setup(x => x.ServiceProvider).Returns(_serviceProvider);

        _service = new ScheduledPipelineService(
            _serviceScopeFactoryMock.Object,
            _configMock.Object,
            _azureFoundryOptionsMock.Object,
            _localFileStoreMock.Object,
            _localFileDiscoveryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task StartAsync_WithValidConfiguration_ShouldInitializeSuccessfully() {
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
    public async Task ExecuteImmediateRunAsync_WithNoFiles_ShouldCompleteSuccessfully() {
        // Arrange
        var batchResult = new BatchPipelineResult {
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
    public async Task ExecuteImmediateRunAsync_WithCsvAndPdfFiles_ShouldSkipLegacyPdfAndProcessCsv() {
        // Arrange
        SetScheduledFiles("test.csv", "manual.pdf");

        var batchResult = new BatchPipelineResult {
            TotalFiles = 1,
            ProcessedSuccessfully = 1,
            Failed = 0,
            EndTime = DateTime.UtcNow
        };

        _orchestratorMock.Setup(x => x.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        // Act
        var result = await _service.ExecuteImmediateRunAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PipelineStatus.PartiallyCompleted, result.Status);
        Assert.Contains("Processed 1 files successfully", result.Message);
        Assert.Contains("Skipped 1 legacy scheduled PDF file(s)", result.Message);
        Assert.Contains(result.Warnings, warning => warning.Contains("local Python processor", StringComparison.OrdinalIgnoreCase));

        // Verify orchestrator was called with correct requests
        _orchestratorMock.Verify(x => x.ProcessBatchAsync(
            It.Is<IEnumerable<DataPipelineRequest>>(requests =>
                requests.Count() == 1 &&
                requests.All(r => r.FileType == FileType.CSV)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteImmediateRunAsync_WithOnlyPdfFiles_ShouldSkipLegacyPdfBeforeOrchestrator() {
        // Arrange
        SetScheduledFiles("manual.pdf");

        // Act
        var result = await _service.ExecuteImmediateRunAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PipelineStatus.Completed, result.Status);
        Assert.Contains("Skipped 1 legacy scheduled PDF file(s)", result.Message);
        Assert.Contains(result.Warnings, warning => warning.Contains("ingestion jobs/local Python processor", StringComparison.OrdinalIgnoreCase));
        _orchestratorMock.Verify(x => x.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteImmediateRunAsync_WithOnlyPdfFiles_ShouldCountRunAsSuccessfulSkip() {
        // Arrange
        SetScheduledFiles("manual.pdf");

        // Act
        await _service.ExecuteImmediateRunAsync(CancellationToken.None);

        // Assert
        var stats = await _service.GetProcessingStatsAsync();
        Assert.Equal(1, stats.TotalScheduledRuns);
        Assert.Equal(1, stats.SuccessfulRuns);
        Assert.Equal(0, stats.FailedRuns);
        Assert.Equal(0, stats.FilesProcessedInLastRun);
        Assert.Equal(PipelineStatus.Completed, stats.LastExecutionStatus);
        Assert.True(stats.AverageProcessingTime >= TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteImmediateRunAsync_WithProcessingErrors_ShouldHandleGracefully() {
        // Arrange
        SetScheduledFiles("test.csv");

        var batchResult = new BatchPipelineResult {
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
    public async Task ExecuteImmediateRunAsync_WithException_ShouldReturnFailedResult() {
        // Arrange
        SetScheduledFiles("test.csv");

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
    public async Task GetProcessingStatsAsync_ShouldReturnValidStats() {
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
    public async Task UpdateScheduleAsync_WithValidCronExpression_ShouldUpdateSuccessfully() {
        // Arrange
        var newConfig = new ProcessingScheduleConfig {
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
    public async Task UpdateScheduleAsync_WithSixFieldCronExpression_ShouldUpdateSuccessfully() {
        // Arrange
        var newConfig = new ProcessingScheduleConfig {
            CronExpression = "0 0 2 * * *", // Daily at 2 AM including seconds
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
        Assert.Equal(2, nextExecution.Value.Hour);
        Assert.Equal(0, nextExecution.Value.Minute);
        Assert.Equal(0, nextExecution.Value.Second);
    }

    [Fact]
    public async Task UpdateScheduleAsync_WithInvalidCronExpression_ShouldHandleGracefully() {
        // Arrange
        var invalidConfig = new ProcessingScheduleConfig {
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
    public async Task UpdateScheduleAsync_WithDisabledSchedule_ShouldDisableExecution() {
        // Arrange
        var disabledConfig = new ProcessingScheduleConfig {
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
    [InlineData("unknown.txt", FileType.Unknown)]
    public async Task ExecuteImmediateRunAsync_ShouldDetectSupportedFileTypes(string fileName, FileType expectedType) {
        ArgumentNullException.ThrowIfNull(fileName);

        // Arrange
        SetScheduledFiles(fileName);

        var batchResult = new BatchPipelineResult {
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
        if (expectedType == FileType.Unknown) {
            // Unknown file types should be ignored
            Assert.Contains("No files found to process", result.Message);
        }
        else {
            // Known file types should be processed
            _orchestratorMock.Verify(x => x.ProcessBatchAsync(
                It.Is<IEnumerable<DataPipelineRequest>>(requests =>
                    requests.Any(r => r.FileType == expectedType)),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task ExecuteImmediateRunAsync_WithPdfFileAndDocumentIntelligenceEnabled_ShouldProcessPdf() {
        // Arrange
        SetScheduledFiles("manual.pdf");

        var services = new ServiceCollection();
        services.AddSingleton(_orchestratorMock.Object);
        var serviceProvider = services.BuildServiceProvider();
        _serviceScopeMock.Setup(x => x.ServiceProvider).Returns(serviceProvider);
        _azureFoundryOptionsMock.Setup(x => x.Value).Returns(new AzureFoundryOptions {
            DocumentIntelligenceEndpoint = "https://example.cognitiveservices.azure.com/"
        });

        using var enabledService = new ScheduledPipelineService(
            _serviceScopeFactoryMock.Object,
            _configMock.Object,
            _azureFoundryOptionsMock.Object,
            _localFileStoreMock.Object,
            _localFileDiscoveryMock.Object,
            _loggerMock.Object);

        var batchResult = new BatchPipelineResult {
            TotalFiles = 1,
            ProcessedSuccessfully = 1,
            Failed = 0,
            EndTime = DateTime.UtcNow
        };

        _orchestratorMock.Setup(x => x.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        // Act
        var result = await enabledService.ExecuteImmediateRunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(PipelineStatus.Completed, result.Status);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("legacy scheduled PDF", StringComparison.OrdinalIgnoreCase));
        _orchestratorMock.Verify(x => x.ProcessBatchAsync(
            It.Is<IEnumerable<DataPipelineRequest>>(requests =>
                requests.Count() == 1 &&
                requests.Single().FileType == FileType.PDF),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteImmediateRunAsync_WithCancellation_ShouldHandleCancellationGracefully() {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () => {
            await _service.ExecuteImmediateRunAsync(cancellationTokenSource.Token);
        });
    }

    [Fact]
    public async Task StatsTracking_ShouldUpdateCorrectlyAfterExecution() {
        // Arrange
        SetScheduledFiles("test.csv");

        _orchestratorMock.Setup(x => x.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new BatchPipelineResult {
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

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (disposing) {
            _service?.Dispose();
            (_serviceProvider as IDisposable)?.Dispose();
        }
    }

    private void SetScheduledFiles(params string[] fileNames) {
        var filePaths = fileNames
            .Select(fileName => Path.Combine(ScheduledDirectory, fileName))
            .ToArray();
        _localFileDiscoveryMock
            .Setup(x => x.GetTopLevelFilePathsAsync(ScheduledDirectory, It.IsAny<CancellationToken>()))
            .ReturnsAsync(filePaths);
    }

    /// <summary>Exposes the protected BackgroundService.ExecuteAsync for direct invocation in tests.</summary>
    private sealed class TestableScheduledPipelineService : ScheduledPipelineService {
        public TestableScheduledPipelineService(
            IServiceScopeFactory serviceScopeFactory,
            IOptions<ScheduledProcessingConfiguration> config,
            IOptions<AzureFoundryOptions> azureFoundryOptions,
            ILocalFileStore localFileStore,
            ILocalFileDiscovery localFileDiscovery,
            ILogger<ScheduledPipelineService> logger)
            : base(serviceScopeFactory, config, azureFoundryOptions, localFileStore, localFileDiscovery, logger) {
        }

        public Task InvokeExecuteAsync(CancellationToken stoppingToken) => ExecuteAsync(stoppingToken);
    }

    [Fact]
    public async Task ExecuteAsync_WhenScheduleDisabled_ReturnsImmediatelyWithoutProcessing() {
        // Arrange
        var configMock = new Mock<IOptions<ScheduledProcessingConfiguration>>();
        configMock.Setup(x => x.Value).Returns(new ScheduledProcessingConfiguration {
            DefaultCronExpression = "0 2 * * *",
            IsEnabledByDefault = false,
            BaseDirectory = "/scheduled-root"
        });

        using var service = new TestableScheduledPipelineService(
            _serviceScopeFactoryMock.Object,
            configMock.Object,
            _azureFoundryOptionsMock.Object,
            _localFileStoreMock.Object,
            _localFileDiscoveryMock.Object,
            _loggerMock.Object);

        // Act
        await service.InvokeExecuteAsync(CancellationToken.None);

        // Assert
        _orchestratorMock.Verify(x => x.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCronCannotBeParsed_WaitsThenExitsGracefullyOnCancellation() {
        // Arrange: an invalid cron expression leaves _schedule null, so ExecuteAsync's
        // "no next scheduled time" branch is exercised (falls back to a 1-minute wait,
        // which we cut short via cancellation instead of actually waiting a minute).
        var configMock = new Mock<IOptions<ScheduledProcessingConfiguration>>();
        configMock.Setup(x => x.Value).Returns(new ScheduledProcessingConfiguration {
            DefaultCronExpression = "not a valid cron",
            IsEnabledByDefault = true,
            BaseDirectory = "/scheduled-root"
        });

        using var service = new TestableScheduledPipelineService(
            _serviceScopeFactoryMock.Object,
            configMock.Object,
            _azureFoundryOptionsMock.Object,
            _localFileStoreMock.Object,
            _localFileDiscoveryMock.Object,
            _loggerMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act: should return (via the internal OperationCanceledException handler) rather than throw.
        await service.InvokeExecuteAsync(cts.Token);

        // Assert
        _orchestratorMock.Verify(x => x.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenScheduleEnabled_ExecutesScheduledProcessingAndStopsOnCancellation() {
        // Arrange: a per-second cron so the loop's scheduled-processing branch runs at least
        // once within the cancellation window, then the loop exits cleanly on cancellation.
        var configMock = new Mock<IOptions<ScheduledProcessingConfiguration>>();
        configMock.Setup(x => x.Value).Returns(new ScheduledProcessingConfiguration {
            DefaultCronExpression = "* * * * * *",
            IsEnabledByDefault = true,
            BaseDirectory = "/scheduled-root"
        });

        _localFileDiscoveryMock
            .Setup(x => x.GetTopLevelFilePathsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        using var service = new TestableScheduledPipelineService(
            _serviceScopeFactoryMock.Object,
            configMock.Object,
            _azureFoundryOptionsMock.Object,
            _localFileStoreMock.Object,
            _localFileDiscoveryMock.Object,
            _loggerMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1800));

        // Act
        await service.InvokeExecuteAsync(cts.Token);

        // Assert
        var stats = await service.GetProcessingStatsAsync();
        Assert.True(stats.TotalScheduledRuns > 0);
    }
}
