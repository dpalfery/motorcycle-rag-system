namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Optional configuration overrides passed when starting a Fabric ingestion job.
/// </summary>
public sealed record IngestionJobConfiguration
{
    /// <summary>
    /// Whether to extract Graph RAG relationships (nodes and edges) during ingestion.
    /// </summary>
    public bool ExtractGraphRelationships { get; init; } = true;

    /// <summary>
    /// Whether to run OCR on scanned/image-based pages.
    /// </summary>
    public bool OcrEnabled { get; init; } = true;
}
