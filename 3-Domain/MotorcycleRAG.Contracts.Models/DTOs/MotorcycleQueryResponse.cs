using System;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Response model for motorcycle queries
/// </summary>
public class MotorcycleQueryResponse {
    public string ResponseType { get; set; } = "Answer";

    [Required]
    public string Response { get; set; } = string.Empty;

    public QueryClarificationSuggestion[] Suggestions { get; set; } = Array.Empty<QueryClarificationSuggestion>();

    public SearchResult[] Sources { get; set; } = Array.Empty<SearchResult>();

    public QueryMetrics Metrics { get; set; } = new();

    [Required]
    public string QueryId { get; set; } = string.Empty;

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    public string ModelUsed { get; set; } = string.Empty;
}
