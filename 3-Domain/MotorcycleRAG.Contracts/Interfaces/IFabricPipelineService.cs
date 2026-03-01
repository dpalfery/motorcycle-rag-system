namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Abstraction for triggering and monitoring Microsoft Fabric pipeline runs.
/// Implemented in the Persistence layer; Application layer depends only on this interface.
/// </summary>
public interface IFabricPipelineService
{
    /// <summary>
    /// Triggers a Fabric data pipeline run for the given upload blob key.
    /// </summary>
    /// <param name="uploadId">The opaque blob key returned by the upload endpoint. Never a filesystem path.</param>
    /// <param name="documentType">Document type: "manual-pdf" or "spec-dataset".</param>
    /// <param name="pipelineId">The Fabric pipeline item ID to trigger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Fabric run ID assigned by the Fabric REST API.</returns>
    Task<string> TriggerPipelineAsync(
        string uploadId,
        string documentType,
        string pipelineId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls the Fabric REST API for the current status of a pipeline run.
    /// </summary>
    /// <param name="fabricRunId">The run ID returned by <see cref="TriggerPipelineAsync"/>.</param>
    /// <param name="pipelineId">The Fabric pipeline item ID that was triggered.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The run status string as returned by Fabric (e.g. "Running", "Succeeded", "Failed").</returns>
    Task<string> GetRunStatusAsync(
        string fabricRunId,
        string pipelineId,
        CancellationToken cancellationToken = default);
}
