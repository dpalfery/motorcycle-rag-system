using System;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Manual/PDF citation locator for document-based sources
/// </summary>
public class ManualPdfCitationLocator
{
    /// <summary>
    /// Document identifier
    /// </summary>
    [Required]
    [StringLength(100)]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Document title
    /// </summary>
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Page number in the document
    /// </summary>
    [Range(1, int.MaxValue)]
    public int PageNumber { get; set; } = 1;

    /// <summary>
    /// Page range (e.g., "5", "5-7")
    /// </summary>
    [StringLength(20)]
    public string? PageRange { get; set; }

    /// <summary>
    /// Primary section heading (highest-level heading on the page)
    /// </summary>
    [StringLength(200)]
    public string? PrimarySection { get; set; }

    /// <summary>
    /// Hierarchy level (1=Chapter, 2=Section, 3=Subsection)
    /// </summary>
    public int? SectionLevel { get; set; }

    /// <summary>
    /// All section headings in the hierarchy path
    /// </summary>
    public string[] SectionHeadings { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Table caption (for table chunks)
    /// </summary>
    [StringLength(200)]
    public string? TableCaption { get; set; }

    /// <summary>
    /// Chunk index within the section
    /// </summary>
    public int? ChunkIndex { get; set; }

    /// <summary>
    /// Section or chapter name (legacy field, use PrimarySection for new data)
    /// </summary>
    [StringLength(100)]
    public string Section { get; set; } = string.Empty;

    /// <summary>
    /// Figure or table reference
    /// </summary>
    [StringLength(100)]
    public string FigureReference { get; set; } = string.Empty;

    /// <summary>
    /// Document version
    /// </summary>
    [StringLength(50)]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Document publication date
    /// </summary>
    public DateTime? PublicationDate { get; set; }

    /// <summary>
    /// Document source URL
    /// </summary>
    [Url]
    [StringLength(1000)]
    public string SourceUrl { get; set; } = string.Empty;
}

/// <summary>
/// Trust tiers for website sources
/// </summary>
public enum WebsiteTrustTier
{
    /// <summary>
    /// High trust - official manufacturer or industry sites
    /// </summary>
    High,

    /// <summary>
    /// Standard trust - reputable sources
    /// </summary>
    Standard,

    /// <summary>
    /// Low trust - requires verification
    /// </summary>
    Low,

    /// <summary>
    /// Unknown trust level
    /// </summary>
    Unknown
}
