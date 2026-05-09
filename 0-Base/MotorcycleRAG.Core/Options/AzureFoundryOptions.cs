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

    public string OrchestratorAgentName { get; set; } = string.Empty;
    public string VectorSearchAgentName { get; set; } = string.Empty;
    public string WebSearchAgentName { get; set; } = string.Empty;
    public string PDFSearchAgentName { get; set; } = string.Empty;
    public string GraphQueryAgentName { get; set; } = string.Empty;

    public string OrchestratorAgentVersion { get; set; } = string.Empty;
    public string VectorSearchAgentVersion { get; set; } = string.Empty;
    public string WebSearchAgentVersion { get; set; } = string.Empty;
    public string PDFSearchAgentVersion { get; set; } = string.Empty;
    public string GraphQueryAgentVersion { get; set; } = string.Empty;
}
