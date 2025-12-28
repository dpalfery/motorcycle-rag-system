using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models;

/// <summary>
/// Represents a search result from any search agent
/// </summary>
public class SearchResult
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    [Range(0.0, 1.0)]
    public float RelevanceScore { get; set; }

    [Required]
    public SearchSource Source { get; set; } = new();

    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Timestamp when the result was generated
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Highlighted text snippets for display
    /// </summary>
    public List<string> Highlights { get; set; } = new();
}

/// <summary>
/// Search source information
/// </summary>
public class SearchSource
{
    [Required]
    public SearchAgentType AgentType { get; set; }

    [Required]
    public string SourceName { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Citation information for evidence-based claims
    /// </summary>
    public Citation? Citation { get; set; }
}

/// <summary>
/// Types of search agents
/// </summary>
public enum SearchAgentType
{
    VectorSearch,
    WebSearch,
    PDFSearch,
    QueryPlanner
}

/// <summary>
/// Types of citation sources for evidence-based responses
/// </summary>
public enum CitationSourceType
{
    Dataset,
    Website,
    ManualPdf,
    ManufacturerSpecs,
    IndustryStandard,
    ExpertReview
}

/// <summary>
/// Citation information for evidence-based claims
/// </summary>
public class Citation
{
    /// <summary>
    /// Type of source being cited
    /// </summary>
    [Required]
    public CitationSourceType SourceType { get; set; }

    /// <summary>
    /// Name of the source
    /// </summary>
    [Required]
    [StringLength(200)]
    public string SourceName { get; set; } = string.Empty;

    /// <summary>
    /// URL to the original source
    /// </summary>
    [Url]
    [StringLength(1000)]
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Page number in the source document
    /// </summary>
    public int? PageNumber { get; set; }

    /// <summary>
    /// Section or chapter name
    /// </summary>
    [StringLength(100)]
    public string Section { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score for this citation (0.0 to 1.0)
    /// </summary>
    [Range(0.0, 1.0)]
    public float ConfidenceScore { get; set; } = 1.0f;

    /// <summary>
    /// Whether this citation has been verified
    /// </summary>
    public bool Verified { get; set; } = false;

    /// <summary>
    /// Method used for verification
    /// </summary>
    [StringLength(200)]
    public string VerificationMethod { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when citation was verified
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>
    /// Additional metadata about the citation
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Source-specific locator information
    /// </summary>
    public object? Locator { get; set; }
}

/// <summary>
/// Dataset citation locator for structured data sources
/// </summary>
public class DatasetCitationLocator
{
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

/// <summary>
/// Website citation locator for web-based sources
/// </summary>
public class WebsiteCitationLocator
{
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
    /// Section or chapter name
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
    High,
    Standard,
    Low,
    Unknown
}
