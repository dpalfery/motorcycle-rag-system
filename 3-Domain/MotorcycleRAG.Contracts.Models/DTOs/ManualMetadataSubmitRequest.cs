using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Request body for POST /api/ingestion/jobs/{jobId}/metadata.
/// Submits manually-entered metadata JSON for a job that is paused in the
/// <c>AwaitingMetadata</c> state. The JSON string is validated server-side before being
/// persisted to <see cref="Domain.Entities.IngestionJob.MetadataJson"/> and used to resume
/// the pipeline.
/// </summary>
public sealed record ManualMetadataSubmitRequest
{
    /// <summary>
    /// A JSON string containing motorcycle metadata with the expected shape:
    /// <c>{ "make": "...", "model": "...", "year": 2023, "category": "...", "tags": [...] }</c>.
    /// The string is parsed server-side; invalid JSON is rejected with a 400 response.
    /// </summary>
    [Required(ErrorMessage = "metadataJson is required.")]
    [MaxLength(10_000, ErrorMessage = "metadataJson must not exceed 10,000 characters.")]
    public string MetadataJson { get; init; } = string.Empty;
}
