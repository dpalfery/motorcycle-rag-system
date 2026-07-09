namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Response shape for GET /api/ingestion/jobs/{jobId}/metadata.
/// Returns the current metadata (extracted or manually submitted) for an ingestion job,
/// parsed into individual fields plus the raw JSON blob and a fill-rate indicator.
/// </summary>
public sealed record IngestionJobMetadataResponse
{
    /// <summary>The ingestion job identifier.</summary>
    public Guid JobId { get; init; }

    /// <summary>Motorcycle manufacturer (e.g. "Honda"). Null if not yet determined.</summary>
    public string? Make { get; init; }

    /// <summary>Motorcycle model name (e.g. "CBR600RR"). Null if not yet determined.</summary>
    public string? Model { get; init; }

    /// <summary>Model year as an integer. Null if not yet determined.</summary>
    public int? Year { get; init; }

    /// <summary>Motorcycle category (e.g. "sport", "cruiser"). Null if not yet determined.</summary>
    public string? Category { get; init; }

    /// <summary>Optional list of tags. Empty when no tags are present.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Fraction of required fields (make, model, year, category) that are populated.
    /// Ranges from 0.0 to 1.0.
    /// </summary>
    public double FillRate { get; init; }

    /// <summary>
    /// True when all four required fields are populated (fill rate = 1.0).
    /// False when the metadata is incomplete or empty.
    /// </summary>
    public bool IsComplete { get; init; }

    /// <summary>
    /// The raw metadata JSON blob as stored in the database, or null when no metadata
    /// has been recorded. Useful for the admin UI to pre-fill the edit form.
    /// </summary>
    public string? RawJson { get; init; }
}
