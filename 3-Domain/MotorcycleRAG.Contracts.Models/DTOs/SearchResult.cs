using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Represents a search result from any search agent
/// </summary>
public class SearchResult {
    [Required]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [Range(0.0, 1.0)]
    public float RelevanceScore { get; set; }

    [Required]
    public SearchSource Source { get; set; } = new();

    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// T9/T10 vector→graph anchor: the canonical <c>IndexedArtifacts.IndexedArtifactId</c>
    /// GUID. Populated by the Azure SDK deserializer from the index's top-level
    /// <c>indexedArtifactId</c> field, then projected into <see cref="Metadata"/> by
    /// <c>AzureSearchQueryService.ExecuteSearchAsync</c> so the retrieval-time hop
    /// (<c>SubAgentToolHandlers</c>) can read it. Null when the index document predates
    /// the T9 schema change. Serialized only when non-null (see <c>WhenWritingNull</c>).
    /// <para>
    /// <see cref="JsonPropertyName"/> is set explicitly because the Azure.Search.Documents
    /// document deserializer uses plain System.Text.Json options (no camelCase/case-
    /// insensitive policy), so PascalCase-by-convention properties do not auto-map to the
    /// index's camelCase fields — the attribute pins the mapping regardless of SDK policy.
    /// </para>
    /// </summary>
    [JsonPropertyName("indexedArtifactId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IndexedArtifactId { get; set; }

    /// <summary>
    /// T9/T10 anchor: the owning <c>IngestionJobId</c>. Same lifecycle as
    /// <see cref="IndexedArtifactId"/>. Projected into <see cref="Metadata"/> as
    /// <c>ingestionJobId</c> for downstream filtering.
    /// </summary>
    [JsonPropertyName("ingestionJobId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IngestionJobId { get; set; }

    /// <summary>
    /// T9/T10 anchor: the parent <c>ManualDocument.SourceContentHash</c> version tag.
    /// Same lifecycle as <see cref="IndexedArtifactId"/>. Projected into
    /// <see cref="Metadata"/> as <c>sourceContentHash</c> for staleness detection.
    /// </summary>
    [JsonPropertyName("sourceContentHash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceContentHash { get; set; }

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
public class SearchSource {
    [Required]
    public SearchAgentType AgentType { get; set; }

    [Required]
    public string SourceName { get; set; } = string.Empty;

    public string? SourceUrl { get; set; }
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
public enum SearchAgentType {
    VectorSearch,
    WebSearch,
    PDFSearch,
    QueryPlanner
}
