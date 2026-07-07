namespace MotorcycleRAG.Contracts.Models.DTOs.Web;

public class ExtractedContent {
    public string Text { get; init; } = string.Empty;
    public string NodePath { get; init; } = string.Empty;
    public bool IsFallback { get; init; }
}
