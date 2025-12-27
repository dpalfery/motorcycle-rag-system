using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.Models;

/// <summary>
/// Types of citation sources for evidence-based responses
/// </summary>
public enum CitationSourceType
{
    /// <summary>
    /// Structured dataset (CSV, database, etc.)
    /// </summary>
    Dataset,
    
    /// <summary>
    /// Web page or online article
    /// </summary>
    Website,
    
    /// <summary>
    /// PDF manual or technical document
    /// </summary>
    ManualPdf,
    
    /// <summary>
    /// Manufacturer official specifications
    /// </summary>
    ManufacturerSpecs,
    
    /// <summary>
    /// Industry standard or certification
    /// </summary>
    IndustryStandard,
    
    /// <summary>
    /// Expert review or analysis
    /// </summary>
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

/// <summary>
/// Request model for motorcycle queries
/// </summary>
public class MotorcycleQueryRequest
{
    [Required]
    [StringLength(1000)]
    public string Query { get; set; } = string.Empty;

    public SearchPreferences Preferences { get; set; } = new();

    [StringLength(100)]
    public string UserId { get; set; } = string.Empty;

    public QueryContext Context { get; set; } = new();
}

/// <summary>
/// Response model for motorcycle queries
/// </summary>
public class MotorcycleQueryResponse
{
    [Required]
    public string Response { get; set; } = string.Empty;

    public SearchResult[] Sources { get; set; } = Array.Empty<SearchResult>();

    public QueryMetrics Metrics { get; set; } = new();

    [Required]
    public string QueryId { get; set; } = string.Empty;

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Search preferences for customizing search behavior
/// </summary>
public class SearchPreferences
{
    public bool IncludeWebSources { get; set; } = true;
    public bool IncludePDFSources { get; set; } = true;
    public int MaxResults { get; set; } = 10;
    public float MinRelevanceScore { get; set; } = 0.5f;
    public List<string> PreferredSources { get; set; } = new();
}

/// <summary>
/// Query context for additional information
/// </summary>
public class QueryContext
{
    public string SessionId { get; set; } = string.Empty;
    public List<string> PreviousQueries { get; set; } = new();
    public Dictionary<string, object> UserPreferences { get; set; } = new();
    public string Language { get; set; } = "en";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool RequiresMultiModal { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, object> AdditionalProperties { get; set; } = new();
}

/// <summary>
/// Query performance metrics with caching and optimization data
/// </summary>
public class QueryMetrics
{
    public TimeSpan TotalDuration { get; set; }
    public TimeSpan VectorSearchDuration { get; set; }
    public TimeSpan WebSearchDuration { get; set; }
    public TimeSpan PDFSearchDuration { get; set; }
    public int TokensUsed { get; set; }
    public decimal EstimatedCost { get; set; }
    public int ResultsFound { get; set; }
    
    // Performance optimization metrics
    public int ProcessingTimeMs { get; set; }
    public bool CacheHit { get; set; }
    public bool MultiModalProcessed { get; set; }
    public int SourcesSearched { get; set; }
    public SearchPatternMetrics? SearchPattern { get; set; }
}

/// <summary>
/// Metrics for the sequential search pattern execution
/// </summary>
public class SearchPatternMetrics
{
    public bool VectorSearchExecuted { get; set; }
    public bool WebSearchExecuted { get; set; }
    public bool PDFSearchExecuted { get; set; }
    public TimeSpan VectorSearchTime { get; set; }
    public TimeSpan WebSearchTime { get; set; }
    public TimeSpan PDFSearchTime { get; set; }
    public int VectorResultsFound { get; set; }
    public int WebResultsFound { get; set; }
    public int PDFResultsFound { get; set; }
}
