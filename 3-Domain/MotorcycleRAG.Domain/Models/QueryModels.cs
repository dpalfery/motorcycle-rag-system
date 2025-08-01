using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

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
}

/// <summary>
/// Query performance metrics
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
}