using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.MobileApp.Models;

public class QueryRequest
{
    [Required]
    [StringLength(10000, MinimumLength = 1)]
    public string Query { get; set; } = string.Empty;

    public string? UserId { get; set; }

    public QueryPreferences? Preferences { get; set; }

    public QueryContext? Context { get; set; }
}

public class QueryPreferences
{
    public bool? IncludeWebSources { get; set; }
    public bool? IncludePDFSources { get; set; }
    public int? MaxResults { get; set; }
    public decimal? MinRelevanceScore { get; set; }
    public List<string>? PreferredSources { get; set; }
}

public class QueryContext
{
    public string? SessionId { get; set; }
    public List<string>? PreviousQueries { get; set; }
    public Dictionary<string, object>? UserMemory { get; set; }
    public object? UserPreferences { get; set; }
    public string? Language { get; set; }
    public DateTime? Timestamp { get; set; }
    public bool? RequiresMultiModal { get; set; }
    public string? CorrelationId { get; set; }
}
