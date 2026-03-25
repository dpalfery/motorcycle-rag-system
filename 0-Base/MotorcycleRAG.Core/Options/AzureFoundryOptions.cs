using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Root configuration for all Azure Cognitive services used by the application.
/// Binds to the "AzureAI" section in configuration sources.
/// </summary>
public class AzureFoundryOptions
{
    [Url]
    public string FoundryEndpoint { get; set; } = string.Empty;

    [Required, Url]
    public string SearchServiceEndpoint { get; set; } = string.Empty;

    [Required, Url]
    public string DocumentIntelligenceEndpoint { get; set; } = string.Empty;

    [Required]
    public ModelOptions Models { get; set; } = new();

    public RetryOptions Retry { get; set; } = new();

    public string OrchestratorAgentId { get; set; } = string.Empty;
    public string VectorSearchAgentId { get; set; } = string.Empty;
    public string WebSearchAgentId { get; set; } = string.Empty;
    public string PDFSearchAgentId { get; set; } = string.Empty;
}
