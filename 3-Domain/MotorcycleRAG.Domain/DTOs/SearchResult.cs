using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.InternalDTOs;

/// <summary>
/// Internal representation of a search result used only inside the Domain project
/// </summary>
public class DomainSearchResult
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    [Range(0.0, 1.0)]
    public float RelevanceScore { get; set; }

    [Required]
    public DomainSearchSource Source { get; set; } = new();

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
/// Internal search source information
/// </summary>
public class DomainSearchSource
{
    [Required]
    public DomainSearchAgentType AgentType { get; set; }

    [Required]
    public string SourceName { get; set; } = string.Empty;

    public string? SourceUrl { get; set; }
    public string DocumentId { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Citation information for evidence-based claims
    /// </summary>
    public DomainCitation? Citation { get; set; }
}

public enum DomainCitationSourceType
{
    Dataset,
    Website,
    ManualPdf,
    ManufacturerSpecs,
    IndustryStandard,
    ExpertReview
}

public sealed class DomainCitation
{
    public DomainCitationSourceType SourceType { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public Uri? SourceUrl { get; set; }
    public int? PageNumber { get; set; }
    public string Section { get; set; } = string.Empty;
    public float ConfidenceScore { get; set; } = 1.0f;
    public bool Verified { get; set; }
    public string VerificationMethod { get; set; } = string.Empty;
    public DateTime? VerifiedAt { get; set; }
    public Dictionary<string, object> Metadata { get; } = new();
    public object? Locator { get; set; }
}

/// <summary>
/// Internal types of search agents
/// </summary>
public enum DomainSearchAgentType
{
    VectorSearch,
    WebSearch,
    PDFSearch,
    QueryPlanner
}
