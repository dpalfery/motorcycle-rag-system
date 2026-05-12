namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Result returned by the validate_question orchestrator tool.
/// </summary>
public sealed class QuestionValidationResult
{
    public string Subject { get; set; } = "Unknown";

    public double Confidence { get; set; }

    public string NormalizedQuery { get; set; } = string.Empty;

    public bool MaySearch { get; set; } = true;

    public string ResponseType { get; set; } = "Answer";

    public string ClarificationQuestion { get; set; } = string.Empty;

    public QueryClarificationSuggestion[] Suggestions { get; set; } = Array.Empty<QueryClarificationSuggestion>();
}
