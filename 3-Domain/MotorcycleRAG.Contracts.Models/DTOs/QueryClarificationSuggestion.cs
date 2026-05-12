namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// A structured clarification choice that can be shown as a user-selectable action.
/// </summary>
public sealed class QueryClarificationSuggestion
{
    public string Label { get; set; } = string.Empty;

    public string Query { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;
}
