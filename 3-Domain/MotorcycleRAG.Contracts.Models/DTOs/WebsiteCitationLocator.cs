using System;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Website citation locator for web-based sources
/// </summary>
public class WebsiteCitationLocator {
    /// <summary>
    /// Complete URL of the web page
    /// </summary>
    [Required]
    [Url]
    [StringLength(1000)]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Website title
    /// </summary>
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Author or publisher
    /// </summary>
    [StringLength(100)]
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// Publication date
    /// </summary>
    public DateTime? PublicationDate { get; set; }

    /// <summary>
    /// Date when content was accessed
    /// </summary>
    public DateTime AccessedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// HTML section identifier
    /// </summary>
    [StringLength(100)]
    public string SectionId { get; set; } = string.Empty;

    /// <summary>
    /// Trust tier of the website
    /// </summary>
    public WebsiteTrustTier TrustTier { get; set; } = WebsiteTrustTier.Standard;
}
