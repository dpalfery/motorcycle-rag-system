using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for scheduled background processing of data pipelines
/// </summary>
public interface IScheduledPipelineService
{
    /// <summary>
    /// Start the scheduled processing service
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the scheduled processing service
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute an immediate processing run
    /// </summary>
    Task<PipelineExecutionResult> ExecuteImmediateRunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get next scheduled execution time
    /// </summary>
    Task<DateTime?> GetNextExecutionTimeAsync();

    /// <summary>
    /// Get scheduled processing statistics
    /// </summary>
    Task<ScheduledProcessingStats> GetProcessingStatsAsync();

    /// <summary>
    /// Configure processing schedule
    /// </summary>
    Task UpdateScheduleAsync(ProcessingScheduleConfig config);
}