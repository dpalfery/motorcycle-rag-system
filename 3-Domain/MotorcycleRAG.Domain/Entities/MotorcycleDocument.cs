using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.Domain.Entities;

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
#pragma warning disable CA1819 // Properties should not return arrays
    public float[]? ContentVector { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays

    /// <summary>
    /// Timestamp when the document was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the document was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // T055: Locator metadata fields for citation support
    // These fields are populated by PDF processor and indexed for search/filter

    /// <summary>
    /// Primary page number for this chunk
    /// </summary>
    public int? PageNumber { get; set; }

    /// <summary>
    /// Page range (e.g., "5", "5-7")
    /// </summary>
    public string? PageRange { get; set; }

    /// <summary>
    /// Primary section heading (highest-level heading on the page)
    /// </summary>
    public string? PrimarySection { get; set; }

    /// <summary>
    /// Hierarchy level (1=Chapter, 2=Section, 3=Subsection)
    /// </summary>
    public int? SectionLevel { get; set; }

    /// <summary>
    /// All section headings in the hierarchy path
    /// </summary>
    public Collection<string> SectionHeadings { get; } = new();

    /// <summary>
    /// Table caption (for table chunks)
    /// </summary>
    public string? TableCaption { get; set; }

    /// <summary>
    /// Chunk index within the section
    /// </summary>
    public int? ChunkIndex { get; set; }
}
