using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Represents a motorcycle document with vector embedding support
/// </summary>
public class MotorcycleDocument
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    [Required]
    public DocumentType Type { get; set; }

    public DocumentMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Vector embedding for semantic search
    /// </summary>
    public float[]? ContentVector { get; set; }

    /// <summary>
    /// Timestamp when the document was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the document was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Document type enumeration
/// </summary>
public enum DocumentType
{
    Specification,
    Manual,
    WebContent,
    TechnicalDocument,
    MaintenanceGuide
}

/// <summary>
/// Document metadata for additional context
/// </summary>
public class DocumentMetadata
{
    public string SourceFile { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public string Section { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTime PublishedDate { get; set; }
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, object> AdditionalProperties { get; set; } = new();
}