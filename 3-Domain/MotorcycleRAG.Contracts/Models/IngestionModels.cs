using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models;

/// <summary>
/// Represents an ingestion job for tracking data ingestion operations
/// </summary>
public class IngestionJob
{
    [Required]
    public long Id { get; set; }

    [Required]
    [StringLength(128)]
    public string JobId { get; set; } = string.Empty;

    [Required]
    public IngestionJobType JobType { get; set; }

    [Required]
    public IngestionJobStatus Status { get; set; }

    [Required]
    [StringLength(500)]
    public string SourceFilePath { get; set; } = string.Empty;

    [StringLength(500)]
    public string? SourceFileName { get; set; }

    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    public DateTime? EndTime { get; set; }

    public TimeSpan Duration => EndTime?.Subtract(StartTime) ?? TimeSpan.Zero;

    [StringLength(128)]
    public string? UserId { get; set; }

    [StringLength(256)]
    public string? UserEmail { get; set; }

    /// <summary>
    /// Total number of records/documents processed
    /// </summary>
    public int TotalRecordsProcessed { get; set; }

    /// <summary>
    /// Number of records successfully indexed
    /// </summary>
    public int RecordsIndexed { get; set; }

    /// <summary>
    /// Number of records that failed processing
    /// </summary>
    public int RecordsFailed { get; set; }

    /// <summary>
    /// Number of records with warnings
    /// </summary>
    public int RecordsWithWarnings { get; set; }

    /// <summary>
    /// JSON serialized metrics dictionary
    /// </summary>
    public string? MetricsJson { get; set; }

    /// <summary>
    /// JSON serialized errors list
    /// </summary>
    public string? ErrorsJson { get; set; }

    /// <summary>
    /// Error message if job failed
    /// </summary>
    [StringLength(2000)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Additional metadata as JSON
    /// </summary>
    public string? MetadataJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Gets metrics as a dictionary
    /// </summary>
    public Dictionary<string, object> GetMetrics()
    {
        if (string.IsNullOrWhiteSpace(MetricsJson))
        {
            return new Dictionary<string, object>();
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(MetricsJson)
                ?? new Dictionary<string, object>();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Sets metrics from a dictionary
    /// </summary>
    public void SetMetrics(Dictionary<string, object> metrics)
    {
        if (metrics == null || metrics.Count == 0)
        {
            MetricsJson = null;
            return;
        }

        MetricsJson = System.Text.Json.JsonSerializer.Serialize(metrics);
    }

    /// <summary>
    /// Gets errors as a list
    /// </summary>
    public List<string> GetErrors()
    {
        if (string.IsNullOrWhiteSpace(ErrorsJson))
        {
            return new List<string>();
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(ErrorsJson)
                ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Sets errors from a list
    /// </summary>
    public void SetErrors(List<string> errors)
    {
        if (errors == null || errors.Count == 0)
        {
            ErrorsJson = null;
            return;
        }

        ErrorsJson = System.Text.Json.JsonSerializer.Serialize(errors);
    }

    /// <summary>
    /// Gets metadata as a dictionary
    /// </summary>
    public Dictionary<string, object> GetMetadata()
    {
        if (string.IsNullOrWhiteSpace(MetadataJson))
        {
            return new Dictionary<string, object>();
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(MetadataJson)
                ?? new Dictionary<string, object>();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Sets metadata from a dictionary
    /// </summary>
    public void SetMetadata(Dictionary<string, object> metadata)
    {
        if (metadata == null || metadata.Count == 0)
        {
            MetadataJson = null;
            return;
        }

        MetadataJson = System.Text.Json.JsonSerializer.Serialize(metadata);
    }
}

/// <summary>
/// Type of ingestion job
/// </summary>
public enum IngestionJobType
{
    StructuredSpecification,
    PDFManual,
    WebContent,
    Batch,
    Scheduled
}

/// <summary>
/// Status of an ingestion job
/// </summary>
public enum IngestionJobStatus
{
    Queued,
    Processing,
    Indexing,
    Completed,
    Failed,
    Cancelled,
    PartiallyCompleted
}
