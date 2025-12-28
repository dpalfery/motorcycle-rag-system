using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Pipeline;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models;
using Xunit;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.UnitTests.Pipeline;

/// <summary>
/// Reliability tests for PipelineMonitoringService to ensure robust monitoring and alerting
/// </summary>
public class PipelineMonitoringServiceReliabilityTests
{
    private readonly Mock<ITelemetryService> _telemetryServiceMock;
    private readonly Mock<ILogger<PipelineMonitoringService>> _loggerMock;
    private readonly Mock<IOptions<PipelineMonitoringConfiguration>> _configMock;
    private readonly Mock<IIngestionJobRepository> _ingestionJobRepositoryMock;
    private readonly PipelineMonitoringService _service;

    public PipelineMonitoringServiceReliabilityTests()
    {
        _telemetryServiceMock = new Mock<ITelemetryService>();
        _loggerMock = new Mock<ILogger<PipelineMonitoringService>>();
        _configMock = new Mock<IOptions<PipelineMonitoringConfiguration>>();
        _ingestionJobRepositoryMock = new Mock<IIngestionJobRepository>();

        var config = new PipelineMonitoringConfiguration
        {
            AlertsEnabled = true,
            FailureRateThreshold = 0.10,
            LongRunningThreshold = TimeSpan.FromMinutes(30),
            ConsecutiveFailuresThreshold = 3,
            MaxActiveExecutions = 10,
            MaxRecentExecutions = 1000,
            DefaultEmailRecipients = new[] { "admin@test.com" }
        };

        _configMock.Setup(x => x.Value).Returns(config);

        _service = new PipelineMonitoringService(_telemetryServiceMock.Object, _ingestionJobRepositoryMock.Object, _configMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task TrackPipelineStartAsync_ShouldLogAndTrackTelemetry()
    {
        // Arrange
        var executionId = "test-execution-123";
        var pipelineType = PipelineType.CSV;
        var context = new PipelineExecutionContext
        {
            CorrelationId = "correlation-123",
            Source = "API",
            RequestTime = DateTime.UtcNow
        };

        // Act
        await _service.TrackPipelineStartAsync(executionId, pipelineType, context);

        // Assert
        _telemetryServiceMock.Verify(x => x.TrackEvent("PipelineStarted", It.Is<Dictionary<string, string>>(d =>
            d["ExecutionId"] == executionId &&
            d["PipelineType"] == pipelineType.ToString() &&
            d["CorrelationId"] == context.CorrelationId
        )), Times.Once);
    }

    [Fact]
    public async Task TrackPipelineCompletionAsync_ShouldUpdateMetricsAndSendNotification()
    {
        // Arrange
        var executionId = "test-execution-123";
        var pipelineType = PipelineType.CSV;
        var context = new PipelineExecutionContext
        {
            CorrelationId = "correlation-123",
            Source = "API"
        };

        var result = new PipelineExecutionResult
        {
            ExecutionId = executionId,
            Status = PipelineStatus.Completed,
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow,
            ProcessedData = new ProcessedData
            {
                Documents = new List<MotorcycleDocument>
                {
                    new MotorcycleDocument { Id = "doc1" },
                    new MotorcycleDocument { Id = "doc2" }
                }
            },
            IndexingResult = new IndexingResult
            {
                Success = true,
                DocumentsIndexed = 2
            }
        };

        // Start tracking first
        await _service.TrackPipelineStartAsync(executionId, pipelineType, context);

        // Act
        await _service.TrackPipelineCompletionAsync(executionId, result);

        // Assert
        _telemetryServiceMock.Verify(x => x.TrackEvent("PipelineCompleted", It.Is<Dictionary<string, string>>(d =>
            d["ExecutionId"] == executionId &&
            d["Status"] == PipelineStatus.Completed.ToString() &&
            d["DocumentsProcessed"] == "2" &&
            d["DocumentsIndexed"] == "2"
        )), Times.Once);
    }

    [Fact]
    public async Task TrackPipelineFailureAsync_ShouldLogErrorAndSendAlert()
    {
        // Arrange
        var executionId = "test-execution-123";
        var pipelineType = PipelineType.PDF;
        var context = new PipelineExecutionContext
        {
            CorrelationId = "correlation-123",
            Source = "API"
        };
        var exception = new InvalidOperationException("Processing failed");

        // Start tracking first
        await _service.TrackPipelineStartAsync(executionId, pipelineType, context);

        // Act
        await _service.TrackPipelineFailureAsync(executionId, exception, context);

        // Assert
        _telemetryServiceMock.Verify(x => x.TrackException(exception, It.Is<Dictionary<string, string>>(d =>
            d["ExecutionId"] == executionId &&
            d["PipelineType"] == pipelineType.ToString() &&
            d["CorrelationId"] == context.CorrelationId
        )), Times.Once);
    }

    [Fact]
    public async Task GetHealthStatusAsync_ShouldReturnValidHealthStatus()
    {
        // Act
        var healthStatus = await _service.GetHealthStatusAsync();

        // Assert
        Assert.NotNull(healthStatus);
        Assert.NotNull(healthStatus.HealthChecks);
        Assert.True(healthStatus.HealthChecks.Count > 0);
        Assert.Contains(healthStatus.HealthChecks, hc => hc.Name == "ActiveExecutions");
        Assert.Contains(healthStatus.HealthChecks, hc => hc.Name == "FailureRate");
    }

    [Fact]
    public async Task GetDetailedMetricsAsync_ShouldReturnMetricsForTimeWindow()
    {
        // Arrange
        var timeWindow = TimeSpan.FromHours(24);

        // Simulate some pipeline executions
        var executionId1 = "exec-1";
        var executionId2 = "exec-2";
        var context = new PipelineExecutionContext { CorrelationId = "test" };

        await _service.TrackPipelineStartAsync(executionId1, PipelineType.CSV, context);
        await _service.TrackPipelineCompletionAsync(executionId1, new PipelineExecutionResult
        {
            ExecutionId = executionId1,
            Status = PipelineStatus.Completed,
            StartTime = DateTime.UtcNow.AddMinutes(-10),
            EndTime = DateTime.UtcNow.AddMinutes(-5),
            ProcessedData = new ProcessedData { Documents = new List<MotorcycleDocument> { new() } }
        });

        await _service.TrackPipelineStartAsync(executionId2, PipelineType.PDF, context);
        await _service.TrackPipelineFailureAsync(executionId2, new Exception("Test failure"), context);

        // Act
        var metrics = await _service.GetDetailedMetricsAsync(timeWindow);

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(timeWindow, metrics.TimeWindow);
        Assert.NotNull(metrics.Executions);
        Assert.NotNull(metrics.Processing);
        Assert.NotNull(metrics.Performance);
        Assert.True(metrics.Executions.TotalExecutions >= 2);
    }

    [Fact]
    public async Task SendNotificationAsync_WithAlertsEnabled_ShouldSendNotification()
    {
        // Arrange
        var notification = new PipelineNotification
        {
            Type = PipelineNotificationType.ExecutionFailed,
            Title = "Pipeline Failed",
            Message = "Test pipeline execution failed",
            ExecutionId = "test-123",
            PipelineType = PipelineType.CSV,
            Severity = NotificationSeverity.Error,
            Recipients = new List<string> { "admin@test.com" }
        };

        // Act
        await _service.SendNotificationAsync(notification);

        // Assert
        _telemetryServiceMock.Verify(x => x.TrackEvent("NotificationSent", It.Is<Dictionary<string, string>>(d =>
            d["NotificationType"] == notification.Type.ToString() &&
            d["Severity"] == notification.Severity.ToString() &&
            d["ExecutionId"] == notification.ExecutionId
        )), Times.Once);
    }

    [Fact]
    public async Task GetAlertConfigAsync_ShouldReturnCurrentConfiguration()
    {
        // Act
        var config = await _service.GetAlertConfigAsync();

        // Assert
        Assert.NotNull(config);
        Assert.True(config.IsEnabled);
        Assert.Equal(0.10, config.Thresholds.FailureRateThreshold);
        Assert.Equal(TimeSpan.FromMinutes(30), config.Thresholds.LongRunningExecutionThreshold);
        Assert.Equal(3, config.Thresholds.ConsecutiveFailuresThreshold);
    }

    [Fact]
    public async Task UpdateAlertConfigAsync_ShouldUpdateConfiguration()
    {
        // Arrange
        var newConfig = new PipelineAlertConfig
        {
            IsEnabled = false,
            Thresholds = new AlertThresholds
            {
                FailureRateThreshold = 0.20,
                LongRunningExecutionThreshold = TimeSpan.FromMinutes(60),
                ConsecutiveFailuresThreshold = 5
            },
            EmailRecipients = new List<string> { "newadmin@test.com" }
        };

        // Act
        await _service.UpdateAlertConfigAsync(newConfig);

        // Assert
        var updatedConfig = await _service.GetAlertConfigAsync();
        Assert.False(updatedConfig.IsEnabled);
        Assert.Equal(0.20, updatedConfig.Thresholds.FailureRateThreshold);
        Assert.Equal(TimeSpan.FromMinutes(60), updatedConfig.Thresholds.LongRunningExecutionThreshold);
        Assert.Equal(5, updatedConfig.Thresholds.ConsecutiveFailuresThreshold);
        Assert.Contains("newadmin@test.com", updatedConfig.EmailRecipients);
    }

    [Fact]
    public async Task HighFailureRate_ShouldTriggerAlert()
    {
        // Arrange
        var context = new PipelineExecutionContext { CorrelationId = "test" };
        
        // Simulate multiple failures to trigger high failure rate
        for (int i = 0; i < 10; i++)
        {
            var executionId = $"exec-{i}";
            await _service.TrackPipelineStartAsync(executionId, PipelineType.CSV, context);
            
            if (i < 8) // 8 failures out of 10 = 80% failure rate
            {
                await _service.TrackPipelineFailureAsync(executionId, new Exception($"Failure {i}"), context);
            }
            else
            {
                await _service.TrackPipelineCompletionAsync(executionId, new PipelineExecutionResult
                {
                    ExecutionId = executionId,
                    Status = PipelineStatus.Completed,
                    StartTime = DateTime.UtcNow.AddMinutes(-1),
                    EndTime = DateTime.UtcNow
                });
            }
        }

        // Act
        var healthStatus = await _service.GetHealthStatusAsync();

        // Assert
        var failureRateCheck = healthStatus.HealthChecks.FirstOrDefault(hc => hc.Name == "FailureRate");
        Assert.NotNull(failureRateCheck);
        // With 80% failure rate, should be unhealthy (threshold is 10%)
        Assert.Equal(HealthCheckStatus.Unhealthy, failureRateCheck.Status);
    }

    [Fact]
    public async Task LongRunningExecution_ShouldBeDetectedInHealthCheck()
    {
        // Arrange
        var executionId = "long-running-exec";
        var context = new PipelineExecutionContext { CorrelationId = "test" };

        // Start a pipeline but don't complete it (simulating long-running)
        await _service.TrackPipelineStartAsync(executionId, PipelineType.PDF, context);

        // Act
        var healthStatus = await _service.GetHealthStatusAsync();

        // Assert
        Assert.NotNull(healthStatus);
        var activeExecutionsCheck = healthStatus.HealthChecks.FirstOrDefault(hc => hc.Name == "ActiveExecutions");
        Assert.NotNull(activeExecutionsCheck);
        Assert.True(activeExecutionsCheck.Data.ContainsKey("ActiveCount"));
        Assert.True((int)activeExecutionsCheck.Data["ActiveCount"] >= 1);
    }

    [Theory]
    [InlineData(NotificationSeverity.Info, false)]
    [InlineData(NotificationSeverity.Warning, true)]
    [InlineData(NotificationSeverity.Error, true)]
    [InlineData(NotificationSeverity.Critical, true)]
    public async Task SendNotificationAsync_ShouldRespectSeveritySettings(NotificationSeverity severity, bool shouldSend)
    {
        // Arrange
        var notification = new PipelineNotification
        {
            Type = PipelineNotificationType.SystemAlert,
            Title = "Test Alert",
            Message = "Test message",
            Severity = severity,
            Recipients = new List<string> { "test@test.com" }
        };

        // Act
        await _service.SendNotificationAsync(notification);

        // Assert
        if (shouldSend)
        {
            _telemetryServiceMock.Verify(x => x.TrackEvent("NotificationSent", It.IsAny<Dictionary<string, string>>()), Times.Once);
        }
        else
        {
            _telemetryServiceMock.Verify(x => x.TrackEvent("NotificationSent", It.IsAny<Dictionary<string, string>>()), Times.Never);
        }
    }
}
