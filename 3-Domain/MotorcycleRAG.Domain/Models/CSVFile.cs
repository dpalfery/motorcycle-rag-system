using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Represents a CSV file for processing motorcycle specifications
/// </summary>
public class CSVFile
{
    [Required]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public Stream Content { get; set; } = Stream.Null;

    /// <summary>
    /// Indicates if the first line contains headers
    /// </summary>
    public bool HasHeaders { get; set; } = true;

    /// <summary>
    /// Delimiter used in the CSV file
    /// </summary>
    public string Delimiter { get; set; } = ",";

    /// <summary>
    /// Encoding of the CSV file
    /// </summary>
    public string Encoding { get; set; } = "UTF-8";

    /// <summary>
    /// Maximum number of columns expected (up to 100+)
    /// </summary>
    public int MaxColumns { get; set; } = 150;

    /// <summary>
    /// Additional metadata about the CSV file
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Source information for tracking
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Upload timestamp
    /// </summary>
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Configuration for CSV processing
/// </summary>
public class CSVProcessingConfiguration
{
    /// <summary>
    /// Number of rows to process in each chunk
    /// </summary>
    public int ChunkSize { get; set; } = 50;

    /// <summary>
    /// Maximum number of rows to process
    /// </summary>
    public int MaxRows { get; set; } = 10000;

    /// <summary>
    /// Fields to use for generating embeddings
    /// </summary>
    public List<string> EmbeddingFields { get; set; } = new();

    /// <summary>
    /// Fields that identify unique motorcycles
    /// </summary>
    public List<string> IdentifierFields { get; set; } = new() { "Make", "Model", "Year" };

    /// <summary>
    /// Whether to preserve relational integrity during chunking
    /// </summary>
    public bool PreserveRelationalIntegrity { get; set; } = true;
}