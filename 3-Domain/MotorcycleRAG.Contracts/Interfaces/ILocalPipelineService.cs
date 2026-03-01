namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Abstraction for triggering and monitoring local processing pipeline runs.
/// Implemented in the Persistence layer; Application layer depends only on this interface.
/// </summary>
public interface ILocalPipelineService
{
    /// <summary>
    /// Triggers a local processing pipeline run for the given upload blob key.
    /// </summary>
    /// <param name="uploadId">The opaque blob key returned by the upload endpoint. Never a filesystem path.</param>
    /// <param name="documentType">Document type: "manual-pdf" or "spec-dataset".</param>
    /// <param name="pipelineId">Reserved for interface compatibility; ignored in local mode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The local job ID assigned by the local processing service.</returns>
    Task<string> TriggerPipelineAsync(
        string uploadId,
        string documentType,
        string pipelineId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls the local processing service for the current status of a job.
    /// </summary>
    /// <param name="runId">The job ID returned by <see cref="TriggerPipelineAsync"/>.</param>
    /// <param name="pipelineId">Reserved for interface compatibility; ignored in local mode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The job status string (e.g. "processing", "completed", "failed").</returns>
    Task<string> GetRunStatusAsync(
        string runId,
        string pipelineId,
        CancellationToken cancellationToken = default);
}
