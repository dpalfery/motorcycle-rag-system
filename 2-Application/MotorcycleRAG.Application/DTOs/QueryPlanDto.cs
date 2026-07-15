using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Application.DTOs;

/// <summary>
/// Query-planner output used within the Application layer.
/// </summary>
public class QueryPlanDto
{
    [Required]
    public string OriginalQuery { get; set; } = string.Empty;

    [Required]
    public Collection<string> SubQueries { get; } = new();

    public bool UseWebSearch { get; set; } = true;

    public bool RunParallel { get; set; } = true;
}
