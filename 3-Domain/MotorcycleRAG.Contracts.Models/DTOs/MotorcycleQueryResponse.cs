using System;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Response model for motorcycle queries
/// </summary>
public class MotorcycleQueryResponse {
    [Required]
    public string Response { get; set; } = string.Empty;

    public SearchResult[] Sources { get; set; } = Array.Empty<SearchResult>();

    public QueryMetrics Metrics { get; set; } = new();

    [Required]
    public string QueryId { get; set; } = string.Empty;

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
