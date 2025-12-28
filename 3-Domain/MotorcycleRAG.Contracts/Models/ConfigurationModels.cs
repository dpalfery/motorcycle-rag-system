using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models;

/// <summary>
/// Trusted source configuration for web search
/// </summary>
public class TrustedSource
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Url]
    public string Url { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int TrustLevel { get; set; } = 3;

    public bool IsActive { get; set; } = true;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public string SearchUrlTemplate { get; set; } = string.Empty;

    public string ContentSelector { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public float CredibilityScore { get; set; }
}

/// <summary>
/// Web search configuration settings
/// </summary>
public class WebSearchConfiguration
{
    [Range(1, 10)]
    public int MaxConcurrentRequests { get; set; } = 3;

    [Range(100, 100000)]
    public int MinRequestIntervalMs { get; set; } = 1000;

    [Range(5, 120)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    [Range(0, 1)]
    public float MinCredibilityScore { get; set; } = 0.6f;

    [Required]
    public string SearchTermModel { get; set; } = "gpt-4o-mini";

    [Required]
    public string ValidationModel { get; set; } = "gpt-4o-mini";

    public List<TrustedSource> TrustedSources { get; set; } = new();
}

/// <summary>
/// Model configuration for AI services
/// </summary>
public class ModelConfiguration
{
    [Required]
    public string ModelName { get; set; } = string.Empty;

    [Required]
    public string DeploymentName { get; set; } = string.Empty;

    public int MaxTokens { get; set; } = 4096;

    public double Temperature { get; set; } = 0.7;

    public double TopP { get; set; } = 1.0;

    public int FrequencyPenalty { get; set; } = 0;

    public int PresencePenalty { get; set; } = 0;
}

/// <summary>
/// Azure AI configuration settings
/// </summary>
public class AzureAIConfiguration
{
    [Required]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string DeploymentName { get; set; } = string.Empty;

    public string ApiVersion { get; set; } = "2024-02-15-preview";

    public int MaxRetries { get; set; } = 3;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}
