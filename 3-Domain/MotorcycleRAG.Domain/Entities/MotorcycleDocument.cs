using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MotorcycleRAG.Domain.DTOs;
using MotorcycleRAG.Domain.Enums;   

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
