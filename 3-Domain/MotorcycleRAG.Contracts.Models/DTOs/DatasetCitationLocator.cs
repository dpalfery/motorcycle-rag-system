using System;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Dataset citation locator for structured data sources
/// </summary>
public class DatasetCitationLocator {
    /// <summary>
    /// Dataset identifier or name
    /// </summary>
    [Required]
    [StringLength(200)]
    public string DatasetName { get; set; } = string.Empty;

    /// <summary>
    /// Dataset version
    /// </summary>
    [StringLength(50)]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Record or row identifier
    /// </summary>
    [StringLength(100)]
    public string RecordId { get; set; } = string.Empty;

    /// <summary>
    /// Field or column name
    /// </summary>
    [StringLength(100)]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Data source URL
    /// </summary>
    [Url]
    [StringLength(1000)]
    public string DataSourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Data retrieval timestamp
    /// </summary>
    public DateTime RetrievalTimestamp { get; set; } = DateTime.UtcNow;
}
