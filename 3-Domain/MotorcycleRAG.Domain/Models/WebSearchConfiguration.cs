using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

public class WebSearchConfiguration
{
    [Range(1,10)]      public int   MaxConcurrentRequests { get; set; } = 3;
    [Range(100,10000)] public int   MinRequestIntervalMs  { get; set; } = 1000;
    [Range(5,120)]     public int   RequestTimeoutSeconds { get; set; } = 30;
    [Range(0,1)]       public float MinCredibilityScore   { get; set; } = 0.6f;

    [Required] public string SearchTermModel  { get; set; } = "gpt-4o-mini";
    [Required] public string ValidationModel  { get; set; } = "gpt-4o-mini";

    public List<TrustedSource> TrustedSources { get; set; } = new();
}