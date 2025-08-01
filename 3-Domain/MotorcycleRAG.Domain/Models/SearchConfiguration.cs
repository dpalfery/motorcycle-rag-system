using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Search service configuration settings bound to the "Search" section in configuration sources.
/// </summary>
public class SearchConfiguration
{
    [Required]
    public string IndexName { get; set; } = "motorcycle-index";

    [Range(1, 1000)]
    public int BatchSize { get; set; } = 100;

    [Range(1, 100)]
    public int MaxSearchResults { get; set; } = 50;

    public bool EnableHybridSearch { get; set; } = true;

    public bool EnableSemanticRanking { get; set; } = true;
}