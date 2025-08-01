using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Root configuration for all Azure Cognitive services used by the application.
/// Binds to the "AzureAI" section in configuration sources.
/// </summary>
public class AzureAIConfiguration
{
    [Required, Url]
    public string FoundryEndpoint { get; set; } = string.Empty;

    [Required, Url]
    public string OpenAIEndpoint { get; set; } = string.Empty;

    [Required, Url]
    public string SearchServiceEndpoint { get; set; } = string.Empty;

    [Required, Url]
    public string DocumentIntelligenceEndpoint { get; set; } = string.Empty;

    [Required]
    public ModelConfiguration Models { get; set; } = new();

    public RetryConfiguration Retry { get; set; } = new();
}