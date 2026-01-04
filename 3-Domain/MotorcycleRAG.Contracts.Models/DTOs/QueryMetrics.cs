using System;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Query performance metrics with caching and optimization data
/// </summary>
public class QueryMetrics {
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
public class SearchPatternMetrics {
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
