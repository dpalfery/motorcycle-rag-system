using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Represents a search strategy plan produced by the query planner.
/// </summary>
public class QueryPlan
{
    [Required]
    public string OriginalQuery { get; set; } = string.Empty;

    [Required]
    public List<string> SubQueries { get; set; } = new();

    public bool UseWebSearch { get; set; } = true;

    public bool RunParallel { get; set; } = true;
}
