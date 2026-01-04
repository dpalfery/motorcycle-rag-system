using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Configuration for individual AI model selections and parameters.
/// </summary>
public class ModelOptions {
    private const int DefaultMaxTokens = 4096;
    private const int MaxTokensLimit = 32_000;
    private const float DefaultTemperature = 0.1f;
    private const float DefaultTopP = 1.0f;
    private const double MaxTemperature = 2.0;

    [Required] public string ChatModel { get; set; } = "gpt-4o-mini";
    [Required] public string EmbeddingModel { get; set; } = "text-embedding-3-large";
    [Required] public string QueryPlannerModel { get; set; } = "gpt-4o";
    [Required] public string VisionModel { get; set; } = "gpt-4-vision-preview";

    [Range(1, MaxTokensLimit)] public int MaxTokens { get; set; } = DefaultMaxTokens;
    [Range(0, MaxTemperature)] public float Temperature { get; set; } = DefaultTemperature;
    [Range(0, 1)] public float TopP { get; set; } = DefaultTopP;
}
