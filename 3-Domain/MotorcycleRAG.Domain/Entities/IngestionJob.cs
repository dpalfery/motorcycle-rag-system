using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Represents an ingestion job for tracking data ingestion operations
/// </summary>
public class IngestionJob {
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

    // Cached deserialized collections to avoid per-access JSON deserialization
    // These are lazy-initialized and invalidated when the underlying JSON changes
    private Dictionary<string, object>? _cachedMetrics;
    private Collection<string>? _cachedErrors;
    private Dictionary<string, object>? _cachedMetadata;
    private string? _lastMetricsJson;
    private string? _lastErrorsJson;
    private string? _lastMetadataJson;

    /// <summary>
    /// Gets the metrics as a dictionary (read-only, cached from MetricsJson)
    /// </summary>
    public Dictionary<string, object> Metrics {
        get {
            // Return cached value if JSON hasn't changed
            if (_cachedMetrics != null && _lastMetricsJson == MetricsJson) {
                return _cachedMetrics;
            }

            // Deserialize and cache
            var result = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(MetricsJson)) {
                try {
                    var jsonDoc = JsonDocument.Parse(MetricsJson);
                    foreach (var prop in jsonDoc.RootElement.EnumerateObject()) {
                        result[prop.Name] = prop.Value.ToString();
                    }
                }
                catch (JsonException) {
                    // If JSON is invalid, return empty dictionary
                }
                _lastMetricsJson = MetricsJson;
            }

            _cachedMetrics = result;
            return _cachedMetrics;
        }
    }

    /// <summary>
    /// Gets the errors as a collection (read-only, cached from ErrorsJson)
    /// </summary>
    public Collection<string> Errors {
        get {
            // Return cached value if JSON hasn't changed
            if (_cachedErrors != null && _lastErrorsJson == ErrorsJson) {
                return _cachedErrors;
            }

            // Deserialize and cache
            var result = new Collection<string>();
            if (!string.IsNullOrWhiteSpace(ErrorsJson)) {
                try {
                    var jsonDoc = JsonDocument.Parse(ErrorsJson);
                    foreach (var element in jsonDoc.RootElement.EnumerateArray()) {
                        result.Add(element.GetString() ?? string.Empty);
                    }
                }
                catch (JsonException) {
                    // If JSON is invalid, return empty collection
                }
                _lastErrorsJson = ErrorsJson;
            }

            _cachedErrors = result;
            return _cachedErrors;
        }
    }

    /// <summary>
    /// Gets the metadata as a dictionary (read-only, cached from MetadataJson)
    /// </summary>
    public Dictionary<string, object> Metadata {
        get {
            // Return cached value if JSON hasn't changed
            if (_cachedMetadata != null && _lastMetadataJson == MetadataJson) {
                return _cachedMetadata;
            }

            // Deserialize and cache
            var result = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(MetadataJson)) {
                try {
                    var jsonDoc = JsonDocument.Parse(MetadataJson);
                    foreach (var prop in jsonDoc.RootElement.EnumerateObject()) {
                        result[prop.Name] = prop.Value.ToString();
                    }
                }
                catch (JsonException) {
                    // If JSON is invalid, return empty dictionary
                }
                _lastMetadataJson = MetadataJson;
            }

            _cachedMetadata = result;
            return _cachedMetadata;
        }
    }
}
