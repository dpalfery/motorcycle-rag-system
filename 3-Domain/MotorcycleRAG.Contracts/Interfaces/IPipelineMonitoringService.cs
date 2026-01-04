using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for monitoring pipeline executions and sending notifications
/// </summary>
public interface IPipelineMonitoringService
{
    /// <summary>
    /// Track pipeline execution start
    /// </summary>
    Task TrackPipelineStartAsync(string executionId, PipelineType pipelineType, PipelineExecutionContext context);

    /// <summary>
    /// Track pipeline execution completion
    /// </summary>
    Task TrackPipelineCompletionAsync(string executionId, PipelineExecutionResult result);

    /// <summary>
    /// Track pipeline execution failure
    /// </summary>
    Task TrackPipelineFailureAsync(string executionId, Exception exception, PipelineExecutionContext context);

    /// <summary>
    /// Send notification for pipeline events
    /// </summary>
    Task SendNotificationAsync(PipelineNotification notification);

    /// <summary>
    /// Get pipeline health status
    /// </summary>
    Task<PipelineHealthStatus> GetHealthStatusAsync();

    /// <summary>
    /// Get detailed pipeline metrics
    /// </summary>
    Task<DetailedPipelineMetrics> GetDetailedMetricsAsync(TimeSpan timeWindow);

    /// <summary>
    /// Get alert configuration
    /// </summary>
    Task<PipelineAlertConfig> GetAlertConfigAsync();

    /// <summary>
    /// Update alert configuration
    /// </summary>
    Task UpdateAlertConfigAsync(PipelineAlertConfig config);
}
