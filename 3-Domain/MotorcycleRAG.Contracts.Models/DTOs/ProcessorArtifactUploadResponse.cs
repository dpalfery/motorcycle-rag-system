namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Response shape for POST /api/ingestion/artifacts/upload (202 Accepted).
/// Confirms that a processor artifact has been stored in blob storage.
/// </summary>
public sealed record ProcessorArtifactUploadResponse
{
    public string UploadId { get; init; } = string.Empty;
    public string ArtifactType { get; init; } = string.Empty;
    public string BlobPath { get; init; } = string.Empty;
    public string Status { get; init; } = "stored";
}
