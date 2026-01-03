using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Search service configuration settings bound to the "Search" section in configuration sources.
/// </summary>
public class SearchOptions
{
    private const int DefaultBatchSize = 100;
    private const int DefaultMaxSearchResults = 50;
    private const int MaxBatchSize = 1000;
    private const int MaxSearchResultsLimit = 100;

    [Required]
    public string IndexName { get; set; } = "motorcycle-index";

    [Range(1, MaxBatchSize)]
    public int BatchSize { get; set; } = DefaultBatchSize;

    [Range(1, MaxSearchResultsLimit)]
    public int MaxSearchResults { get; set; } = DefaultMaxSearchResults;

    public bool EnableHybridSearch { get; set; } = true;

    public bool EnableSemanticRanking { get; set; } = true;
}
