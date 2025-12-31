namespace MotorcycleRAG.MobileApp.Models;

public class QueryResponse
{
    public string Response { get; set; } = string.Empty;
    public List<SearchResult> Sources { get; set; } = new();
    public QueryMetrics? Metrics { get; set; }
    public string QueryId { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

public class SearchResult
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public decimal RelevanceScore { get; set; }
    public SourceInfo? Source { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public DateTime GeneratedAt { get; set; }
    public List<string>? Highlights { get; set; }
}

public class SourceInfo
{
    public string AgentType { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string? DocumentId { get; set; }
    public DateTime? LastUpdated { get; set; }
}

public class QueryMetrics
{
    public long TotalDurationMs { get; set; }
    public List<string>? AgentsUsed { get; set; }
    public int SourcesCombined { get; set; }
    public int? TokensUsed { get; set; }
}
