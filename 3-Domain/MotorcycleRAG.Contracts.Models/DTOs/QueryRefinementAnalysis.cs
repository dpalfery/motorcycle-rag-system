namespace MotorcycleRAG.Contracts.Models.DTOs;

public class QueryRefinementAnalysis {
    public string OriginalQuery { get; set; } = string.Empty;
    public IReadOnlyList<string> Suggestions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExampleQueries { get; init; } = Array.Empty<string>();
}
