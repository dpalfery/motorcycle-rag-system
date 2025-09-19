using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;
using System.Collections.Concurrent;

namespace MotorcycleRAG.Application.Pipeline;

/// <summary>
/// Service for monitoring pipeline executions and sending notifications
/// </summary>
public class PipelineMonitoringService : IPipelineMonitoringService
{
    private readonly ITelemetryService _telemetryService;
    private readonly ILogger<PipelineMonitoringService> _logger;
    private readonly PipelineMonitoringConfiguration _config;
    
    private readonly ConcurrentDictionary<string, PipelineExecutionTracker> _activeExecutions;
    private readonly List<PipelineExecutionSummary> _recentExecutions;
    private readonly object _recentExecutionsLock = new();
    private PipelineAlertConfig _alertConfig;

    public PipelineMonitoringService(
        ITelemetryService telemetryService,
        IOptions<PipelineMonitoringConfiguration> config,
        ILogger<PipelineMonitoringService> logger)
    {
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _activeExecutions = new ConcurrentDictionary<string, PipelineExecutionTracker>();
        _recentExecutions = new List<PipelineExecutionSummary>();
        
        _alertConfig = new PipelineAlertConfig
        {
            IsEnabled = _config.AlertsEnabled,
            EmailRecipients = _config.DefaultEmailRecipients?.ToList() ?? new List<string>(),
            Thresholds = new AlertThresholds
            {
                FailureRateThreshold = _config.FailureRateThreshold,
                LongRunningExecutionThreshold = _config.LongRunningThreshold,
                ConsecutiveFailuresThreshold = _config.ConsecutiveFailuresThreshold
            }
        };
    }

    public async Task TrackPipelineStartAsync(string executionId, PipelineType pipelineType, PipelineExecutionContext context)
    {
        var tracker = new PipelineExecutionTracker
        {
            ExecutionId = executionId,
            PipelineType = pipelineType,
            Context = context,
            StartTime = DateTime.UtcNow,
            Status = PipelineStatus.Processing
        };

        _activeExecutions[executionId] = tracker;

        _logger.LogInformation("Pipeline execution started: {ExecutionId}, Type: {PipelineType}", 
            executionId, pipelineType);

        // Track telemetry
        _telemetryService.TrackEvent("PipelineStarted", new Dictionary<string, string>
        {
            ["ExecutionId"] = executionId,
            ["PipelineType"] = pipelineType.ToString(),
            ["CorrelationId"] = context.CorrelationId,
            ["Source"] = context.Source
        });

        // Send notification if configured
        if (_alertConfig.IsEnabled && _alertConfig.EnabledSeverities[NotificationSeverity.Info])
        {
            await SendNotificationAsync(new PipelineNotification
            {
                Type = PipelineNotificationType.ExecutionStarted,
                Title = "Pipeline Execution Started",
                Message = $"Pipeline {executionId} of type {pipelineType} has started processing",
                ExecutionId = executionId,
                PipelineType = pipelineType,
                Severity = NotificationSeverity.Info,
                Recipients = _alertConfig.EmailRecipients
            });
        }
    }

    public async Task TrackPipelineCompletionAsync(string executionId, PipelineExecutionResult result)
    {
        if (_activeExecutions.TryRemove(executionId, out var tracker))
        {
            tracker.Status = result.Status;
            tracker.EndTime = result.EndTime ?? DateTime.UtcNow;
            tracker.Duration = tracker.EndTime.Value - tracker.StartTime;
            tracker.DocumentsProcessed = result.ProcessedData?.Documents.Count ?? 0;
            tracker.DocumentsIndexed = result.IndexingResult?.DocumentsIndexed ?? 0;

            // Add to recent executions
            lock (_recentExecutionsLock)
            {
                var summary = new PipelineExecutionSummary
                {
                    ExecutionId = executionId,
                    FileType = tracker.PipelineType switch
                    {
                        PipelineType.CSV => FileType.CSV,
                        PipelineType.PDF => FileType.PDF,
                        _ => FileType.Unknown
                    },
                    Status = result.Status,
                    StartTime = tracker.StartTime,
                    Duration = tracker.Duration,
                    DocumentsProcessed = tracker.DocumentsProcessed,
                    DocumentsIndexed = tracker.DocumentsIndexed
                };

                _recentExecutions.Add(summary);

                // Keep only recent executions (last 1000)
                if (_recentExecutions.Count > _config.MaxRecentExecutions)
                {
                    _recentExecutions.RemoveAt(0);
                }
            }

            _logger.LogInformation("Pipeline execution completed: {ExecutionId}, Status: {Status}, Duration: {Duration}ms",
                executionId, result.Status, tracker.Duration.TotalMilliseconds);

            // Track telemetry
            _telemetryService.TrackEvent("PipelineCompleted", new Dictionary<string, string>
            {
                ["ExecutionId"] = executionId,
                ["Status"] = result.Status.ToString(),
                ["Duration"] = tracker.Duration.TotalMilliseconds.ToString(),
                ["DocumentsProcessed"] = tracker.DocumentsProcessed.ToString(),
                ["DocumentsIndexed"] = tracker.DocumentsIndexed.ToString()
            });

            // Send completion notification
            await SendCompletionNotificationAsync(executionId, result, tracker);

            // Check for alert conditions
            await CheckAlertConditionsAsync();
        }
    }

    public async Task TrackPipelineFailureAsync(string executionId, Exception exception, PipelineExecutionContext context)
    {
        if (_activeExecutions.TryRemove(executionId, out var tracker))
        {
            tracker.Status = PipelineStatus.Failed;
            tracker.EndTime = DateTime.UtcNow;
            tracker.Duration = tracker.EndTime.Value - tracker.StartTime;
            tracker.ErrorMessage = exception.Message;

            // Add to recent executions
            lock (_recentExecutionsLock)
            {
                var summary = new PipelineExecutionSummary
                {
                    ExecutionId = executionId,
                    Status = PipelineStatus.Failed,
                    StartTime = tracker.StartTime,
                    Duration = tracker.Duration,
                    ErrorMessage = exception.Message
                };

                _recentExecutions.Add(summary);

                if (_recentExecutions.Count > _config.MaxRecentExecutions)
                {
                    _recentExecutions.RemoveAt(0);
                }
            }

            _logger.LogError(exception, "Pipeline execution failed: {ExecutionId}, Duration: {Duration}ms",
                executionId, tracker.Duration.TotalMilliseconds);

            // Track telemetry
            _telemetryService.TrackException(exception, new Dictionary<string, string>
            {
                ["ExecutionId"] = executionId,
                ["PipelineType"] = tracker.PipelineType.ToString(),
                ["Duration"] = tracker.Duration.TotalMilliseconds.ToString(),
                ["CorrelationId"] = context.CorrelationId
            });

            // Send failure notification
            await SendNotificationAsync(new PipelineNotification
            {
                Type = PipelineNotificationType.ExecutionFailed,
                Title = "Pipeline Execution Failed",
                Message = $"Pipeline {executionId} failed: {exception.Message}",
                ExecutionId = executionId,
                PipelineType = tracker.PipelineType,
                Severity = NotificationSeverity.Error,
                Recipients = _alertConfig.EmailRecipients,
                Properties = new Dictionary<string, object>
                {
                    ["Exception"] = exception.ToString(),
                    ["Duration"] = tracker.Duration.TotalMilliseconds
                }
            });

            // Check for consecutive failures
            await CheckAlertConditionsAsync();
        }
    }

    public async Task SendNotificationAsync(PipelineNotification notification)
    {
        if (!_alertConfig.IsEnabled || !_alertConfig.EnabledSeverities[notification.Severity])
        {
            return;
        }

        try
        {
            _logger.LogInformation("Sending pipeline notification: {Type}, Severity: {Severity}, ExecutionId: {ExecutionId}",
                notification.Type, notification.Severity, notification.ExecutionId);

            // In a real implementation, this would send emails, Slack messages, etc.
            // For now, just log the notification
            _logger.LogInformation("Pipeline Notification - {Title}: {Message}", 
                notification.Title, notification.Message);

            // Track notification telemetry
            _telemetryService.TrackEvent("NotificationSent", new Dictionary<string, string>
            {
                ["NotificationType"] = notification.Type.ToString(),
                ["Severity"] = notification.Severity.ToString(),
                ["ExecutionId"] = notification.ExecutionId,
                ["Recipients"] = string.Join(",", notification.Recipients)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send pipeline notification: {NotificationId}", notification.Id);
        }
    }

    public async Task<PipelineHealthStatus> GetHealthStatusAsync()
    {
        var healthStatus = new PipelineHealthStatus();
        var healthChecks = new List<HealthCheck>();

        // Check active executions
        var activeCount = _activeExecutions.Count;
        var longRunningCount = _activeExecutions.Values.Count(e => 
            DateTime.UtcNow - e.StartTime > _alertConfig.Thresholds.LongRunningExecutionThreshold);

        healthChecks.Add(new HealthCheck
        {
            Name = "ActiveExecutions",
            Status = activeCount > _config.MaxActiveExecutions ? HealthCheckStatus.Degraded : HealthCheckStatus.Healthy,
            Description = $"{activeCount} active executions, {longRunningCount} long-running",
            Data = new Dictionary<string, object>
            {
                ["ActiveCount"] = activeCount,
                ["LongRunningCount"] = longRunningCount,
                ["MaxAllowed"] = _config.MaxActiveExecutions
            }
        });

        // Check recent failure rate
        var recentFailureRate = await CalculateRecentFailureRateAsync();
        healthChecks.Add(new HealthCheck
        {
            Name = "FailureRate",
            Status = recentFailureRate > _alertConfig.Thresholds.FailureRateThreshold ? 
                HealthCheckStatus.Unhealthy : HealthCheckStatus.Healthy,
            Description = $"Recent failure rate: {recentFailureRate:P2}",
            Data = new Dictionary<string, object>
            {
                ["FailureRate"] = recentFailureRate,
                ["Threshold"] = _alertConfig.Thresholds.FailureRateThreshold
            }
        });

        // Overall health status
        healthStatus.HealthChecks = healthChecks;
        healthStatus.Status = healthChecks.All(h => h.Status == HealthCheckStatus.Healthy) ? 
            OverallHealthStatus.Healthy :
            healthChecks.Any(h => h.Status == HealthCheckStatus.Unhealthy) ? 
                OverallHealthStatus.Unhealthy : OverallHealthStatus.Degraded;

        healthStatus.Metrics = new Dictionary<string, object>
        {
            ["ActiveExecutions"] = activeCount,
            ["RecentFailureRate"] = recentFailureRate,
            ["TotalRecentExecutions"] = _recentExecutions.Count
        };

        return healthStatus;
    }

    public async Task<DetailedPipelineMetrics> GetDetailedMetricsAsync(TimeSpan timeWindow)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime - timeWindow;

        var relevantExecutions = _recentExecutions
            .Where(e => e.StartTime >= startTime && e.StartTime <= endTime)
            .ToList();

        var metrics = new DetailedPipelineMetrics
        {
            TimeWindow = timeWindow,
            StartTime = startTime,
            EndTime = endTime,
            Executions = new ExecutionMetrics
            {
                TotalExecutions = relevantExecutions.Count,
                SuccessfulExecutions = relevantExecutions.Count(e => e.Status == PipelineStatus.Completed),
                FailedExecutions = relevantExecutions.Count(e => e.Status == PipelineStatus.Failed),
                CancelledExecutions = relevantExecutions.Count(e => e.Status == PipelineStatus.Cancelled),
                ExecutionsByType = relevantExecutions.GroupBy(e => e.FileType switch
                {
                    FileType.CSV => PipelineType.CSV,
                    FileType.PDF => PipelineType.PDF,
                    _ => PipelineType.Batch
                }).ToDictionary(g => g.Key, g => g.Count())
            },
            Processing = new ProcessingMetrics
            {
                TotalDocumentsProcessed = relevantExecutions.Sum(e => e.DocumentsProcessed),
                TotalDocumentsIndexed = relevantExecutions.Sum(e => e.DocumentsIndexed),
                DocumentsByType = relevantExecutions.GroupBy(e => e.FileType)
                    .ToDictionary(g => g.Key, g => (long)g.Sum(e => e.DocumentsProcessed)),
                BytesByType = new Dictionary<FileType, long>() // Would need to track file sizes
            },
            Performance = new PerformanceMetrics
            {
                AverageExecutionTime = relevantExecutions.Any() ? 
                    TimeSpan.FromMilliseconds(relevantExecutions.Average(e => e.Duration.TotalMilliseconds)) : 
                    TimeSpan.Zero,
                MedianExecutionTime = relevantExecutions.Any() ? 
                    TimeSpan.FromMilliseconds(relevantExecutions.OrderBy(e => e.Duration).Skip(relevantExecutions.Count / 2).First().Duration.TotalMilliseconds) : 
                    TimeSpan.Zero,
                P95ExecutionTime = relevantExecutions.Any() ? 
                    TimeSpan.FromMilliseconds(relevantExecutions.OrderBy(e => e.Duration).Skip((int)(relevantExecutions.Count * 0.95)).FirstOrDefault()?.Duration.TotalMilliseconds ?? 0) : 
                    TimeSpan.Zero
            }
        };

        return metrics;
    }

    public async Task<PipelineAlertConfig> GetAlertConfigAsync()
    {
        return await Task.FromResult(_alertConfig);
    }

    public async Task UpdateAlertConfigAsync(PipelineAlertConfig config)
    {
        _alertConfig = config ?? throw new ArgumentNullException(nameof(config));
        _logger.LogInformation("Pipeline alert configuration updated");
        await Task.CompletedTask;
    }

    private async Task SendCompletionNotificationAsync(string executionId, PipelineExecutionResult result, PipelineExecutionTracker tracker)
    {
        var notificationType = result.Status == PipelineStatus.Completed 
            ? PipelineNotificationType.ExecutionCompleted 
            : PipelineNotificationType.ExecutionFailed;

        var severity = result.Status switch
        {
            PipelineStatus.Completed => NotificationSeverity.Info,
            PipelineStatus.PartiallyCompleted => NotificationSeverity.Warning,
            PipelineStatus.Failed => NotificationSeverity.Error,
            PipelineStatus.Cancelled => NotificationSeverity.Warning,
            _ => NotificationSeverity.Info
        };

        await SendNotificationAsync(new PipelineNotification
        {
            Type = notificationType,
            Title = $"Pipeline Execution {result.Status}",
            Message = $"Pipeline {executionId} {result.Status.ToString().ToLowerInvariant()} in {tracker.Duration.TotalSeconds:F1} seconds. Processed: {tracker.DocumentsProcessed} documents, Indexed: {tracker.DocumentsIndexed} documents",
            ExecutionId = executionId,
            PipelineType = tracker.PipelineType,
            Severity = severity,
            Recipients = _alertConfig.EmailRecipients,
            Properties = new Dictionary<string, object>
            {
                ["Duration"] = tracker.Duration.TotalMilliseconds,
                ["DocumentsProcessed"] = tracker.DocumentsProcessed,
                ["DocumentsIndexed"] = tracker.DocumentsIndexed,
                ["Status"] = result.Status.ToString()
            }
        });
    }

    private async Task CheckAlertConditionsAsync()
    {
        if (!_alertConfig.IsEnabled)
            return;

        // Check failure rate
        var recentFailureRate = await CalculateRecentFailureRateAsync();
        if (recentFailureRate > _alertConfig.Thresholds.FailureRateThreshold)
        {
            await SendNotificationAsync(new PipelineNotification
            {
                Type = PipelineNotificationType.HighFailureRate,
                Title = "High Failure Rate Alert",
                Message = $"Pipeline failure rate ({recentFailureRate:P2}) exceeds threshold ({_alertConfig.Thresholds.FailureRateThreshold:P2})",
                Severity = NotificationSeverity.Critical,
                Recipients = _alertConfig.EmailRecipients
            });
        }

        // Check for long-running executions
        var longRunningExecutions = _activeExecutions.Values
            .Where(e => DateTime.UtcNow - e.StartTime > _alertConfig.Thresholds.LongRunningExecutionThreshold)
            .ToList();

        foreach (var execution in longRunningExecutions)
        {
            await SendNotificationAsync(new PipelineNotification
            {
                Type = PipelineNotificationType.LongRunningExecution,
                Title = "Long Running Execution Alert",
                Message = $"Pipeline {execution.ExecutionId} has been running for {DateTime.UtcNow - execution.StartTime:hh\\:mm\\:ss}",
                ExecutionId = execution.ExecutionId,
                PipelineType = execution.PipelineType,
                Severity = NotificationSeverity.Warning,
                Recipients = _alertConfig.EmailRecipients
            });
        }
    }

    private async Task<double> CalculateRecentFailureRateAsync()
    {
        var recentExecutions = _recentExecutions
            .Where(e => e.StartTime >= DateTime.UtcNow.AddHours(-1))
            .ToList();

        if (recentExecutions.Count == 0)
            return 0.0;

        var failedCount = recentExecutions.Count(e => e.Status == PipelineStatus.Failed);
        return (double)failedCount / recentExecutions.Count;
    }
}

/// <summary>
/// Tracks active pipeline execution
/// </summary>
internal class PipelineExecutionTracker
{
    public string ExecutionId { get; set; } = string.Empty;
    public PipelineType PipelineType { get; set; }
    public PipelineExecutionContext Context { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public PipelineStatus Status { get; set; }
    public int DocumentsProcessed { get; set; }
    public int DocumentsIndexed { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Configuration for pipeline monitoring
/// </summary>
public class PipelineMonitoringConfiguration
{
    public bool AlertsEnabled { get; set; } = true;
    public double FailureRateThreshold { get; set; } = 0.10;
    public TimeSpan LongRunningThreshold { get; set; } = TimeSpan.FromMinutes(30);
    public int ConsecutiveFailuresThreshold { get; set; } = 3;
    public int MaxActiveExecutions { get; set; } = 10;
    public int MaxRecentExecutions { get; set; } = 1000;
    public string[] DefaultEmailRecipients { get; set; } = Array.Empty<string>();
}