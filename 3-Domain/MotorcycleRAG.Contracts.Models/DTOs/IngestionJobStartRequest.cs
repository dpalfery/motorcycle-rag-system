namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Request body for POST /api/ingestion/jobs — triggers a Microsoft Fabric ingestion pipeline run.
/// The uploadId references a previously uploaded blob; no filesystem paths are accepted.
/// </summary>
public sealed record IngestionJobStartRequest
{
    /// <summary>
    /// Opaque reference to the uploaded file (blob key or upload GUID returned by the upload endpoint).
    /// Must never be a raw filesystem path.
    /// </summary>
    public string UploadId { get; init; } = string.Empty;

    /// <summary>
    /// Type of document: "manual-pdf", "spec-dataset", or "bike-graph".
    /// </summary>
    public string DocumentType { get; init; } = string.Empty;

    /// <summary>
    /// Optional pipeline configuration overrides.
    /// </summary>
    public IngestionJobConfiguration? Configuration { get; init; }
}
