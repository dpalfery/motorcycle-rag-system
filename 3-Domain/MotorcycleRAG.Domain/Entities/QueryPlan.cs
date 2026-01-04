using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Represents a search strategy plan produced by the query planner.
/// </summary>
public class QueryPlan
{
    [Required]
    public string OriginalQuery { get; set; } = string.Empty;

    [Required]
    public Collection<string> SubQueries { get; } = new();

    public bool UseWebSearch { get; set; } = true;

    public bool RunParallel { get; set; } = true;
}
