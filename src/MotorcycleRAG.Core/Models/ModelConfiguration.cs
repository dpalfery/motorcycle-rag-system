using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Configuration for individual AI model selections and parameters.
/// </summary>
public class ModelConfiguration
{
    [Required] public string ChatModel        { get; set; } = "gpt-4o-mini";
    [Required] public string EmbeddingModel   { get; set; } = "text-embedding-3-large";
    [Required] public string QueryPlannerModel{ get; set; } = "gpt-4o";
    [Required] public string VisionModel      { get; set; } = "gpt-4-vision-preview";

    [Range(1, 32_000)] public int   MaxTokens   { get; set; } = 4096;
    [Range(0, 2)]      public float Temperature { get; set; } = 0.1f;
    [Range(0, 1)]      public float TopP        { get; set; } = 1.0f;
}