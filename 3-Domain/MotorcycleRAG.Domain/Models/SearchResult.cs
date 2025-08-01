using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

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