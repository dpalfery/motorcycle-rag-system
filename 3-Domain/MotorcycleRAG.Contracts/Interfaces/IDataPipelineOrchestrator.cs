using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for coordinating ETL pipeline operations for motorcycle data
/// </summary>
public interface IDataPipelineOrchestrator
{
    /// <summary>
    /// Process a single file through the appropriate pipeline
    /// </summary>
    Task<PipelineExecutionResult> ProcessFileAsync(DataPipelineRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process multiple files in batch
    /// </summary>
    Task<BatchPipelineResult> ProcessBatchAsync(IEnumerable<DataPipelineRequest> requests, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get pipeline execution status
    /// </summary>
    Task<PipelineStatus> GetPipelineStatusAsync(string executionId);

    /// <summary>
    /// Get pipeline execution history and metrics
    /// </summary>
    Task<PipelineMetrics> GetPipelineMetricsAsync(TimeSpan? timeWindow = null);

    /// <summary>
    /// Cancel a running pipeline execution
    /// </summary>
    Task<bool> CancelPipelineAsync(string executionId);
}
