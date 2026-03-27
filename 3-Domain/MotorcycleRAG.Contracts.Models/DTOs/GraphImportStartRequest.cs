namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Request body for importing a processed graph artifact that already exists in blob storage.
/// </summary>
public sealed record GraphImportStartRequest
{
    /// <summary>
    /// Upload identifier used by the local processor when it wrote graph-entities/{uploadId}/entities.json.
    /// </summary>
    public string UploadId { get; init; } = string.Empty;
}
