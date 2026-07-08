using System.Text.Json.Serialization;
using MotorcycleRAG.Contracts.Models.Serialization;
using MotorcycleRAG.Domain.ValueObjects;

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

    /// <summary>
    /// Optional motorcycle category override (Dirt, Touring, Sport, Cruiser). When
    /// supplied, ingestion routes chunks to the matching category-partitioned index
    /// without invoking the category classifier. When omitted, the classifier resolves
    /// the category from the bike make/model.
    /// </summary>
    [JsonConverter(typeof(MotorcycleCategoryJsonConverter))]
    public MotorcycleCategory? Category { get; init; }
}
