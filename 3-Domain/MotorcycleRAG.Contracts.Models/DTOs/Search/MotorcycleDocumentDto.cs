using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Contracts.Models.DTOs.Search;

/// <summary>
/// Shared search-index document payload.
/// </summary>
public class MotorcycleDocumentDto
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

    public DocumentMetadataDto Metadata { get; set; } = new();

    /// <summary>
    /// Vector embedding for semantic search.
    /// </summary>
#pragma warning disable CA1819 // Properties should not return arrays
    public float[]? ContentVector { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int? PageNumber { get; set; }

    public string? PageRange { get; set; }

    public string? PrimarySection { get; set; }

    public int? SectionLevel { get; set; }

    public Collection<string> SectionHeadings { get; } = new();

    public string? TableCaption { get; set; }

    public int? ChunkIndex { get; set; }
}
