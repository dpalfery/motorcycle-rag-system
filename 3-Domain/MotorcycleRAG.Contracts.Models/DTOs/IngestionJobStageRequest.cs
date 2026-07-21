namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Request shape for PATCH /api/ingestion/jobs/{jobId}/status.
/// Reports the current pipeline stage and optional chunk count from the local processor.
/// </summary>
public sealed record IngestionJobStageRequest
{
    /// <summary>Pipeline stage name (copying, parsing, chunking, embedding, uploading-chunks, extracting-graph, uploading-graph, completed).</summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>Number of chunks successfully embedded so far. Only meaningful during the embedding stage.</summary>
    public int? ChunksProcessed { get; init; }

    /// <summary>Total number of chunks expected. Only meaningful during the embedding stage.</summary>
    public int? TotalChunks { get; init; }

    /// <summary>Optional failure detail when stage reports a failure.</summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Optional partially-extracted metadata JSON. Sent by the local processor with the
    /// <c>needs-manual-metadata</c> stage so the admin manual-entry form can pre-fill the
    /// fields automated extraction did determine. Ignored for every other stage.
    /// </summary>
    public string? MetadataJson { get; init; }
}
