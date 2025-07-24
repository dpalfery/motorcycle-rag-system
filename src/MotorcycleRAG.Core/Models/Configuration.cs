using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Azure AI Foundry configuration
/// </summary>
public class AzureAIConfiguration
{
    [Required]
    [Url]
    public string FoundryEndpoint { get; set; } = string.Empty;

    [Required]
    [Url]
    public string OpenAIEndpoint { get; set; } = string.Empty;

    [Required]
    [Url]
    public string SearchServiceEndpoint { get; set; } = string.Empty;

    [Required]
    [Url]
    public string DocumentIntelligenceEndpoint { get; set; } = string.Empty;

    [Required]
    public ModelConfiguration Models { get; set; } = new();

    public RetryConfiguration Retry { get; set; } = new();
}

/// <summary>
/// AI model configuration
/// </summary>
public class ModelConfiguration
{
    [Required]
    public string ChatModel { get; set; } = "gpt-4o-mini";

    [Required]
    public string EmbeddingModel { get; set; } = "text-embedding-3-large";

    [Required]
    public string QueryPlannerModel { get; set; } = "gpt-4o";

    [Required]
    public string VisionModel { get; set; } = "gpt-4-vision-preview";

    [Range(1, 32000)]
    public int MaxTokens { get; set; } = 4096;

    [Range(0.0, 2.0)]
    public float Temperature { get; set; } = 0.1f;

    [Range(0.0, 1.0)]
    public float TopP { get; set; } = 1.0f;
}

/// <summary>
/// Retry policy configuration
/// </summary>
public class RetryConfiguration
{
    [Range(1, 10)]
    public int MaxRetries { get; set; } = 3;

    [Range(1, 300)]
    public int BaseDelaySeconds { get; set; } = 2;

    [Range(1, 600)]
    public int MaxDelaySeconds { get; set; } = 60;

    public bool UseExponentialBackoff { get; set; } = true;
}

/// <summary>
/// Search service configuration
/// </summary>
public class SearchConfiguration
{
    [Required]
    public string IndexName { get; set; } = "motorcycle-index";

    [Range(1, 1000)]
    public int BatchSize { get; set; } = 100;

    [Range(1, 100)]
    public int MaxSearchResults { get; set; } = 50;

    public bool EnableHybridSearch { get; set; } = true;
    public bool EnableSemanticRanking { get; set; } = true;
}

/// <summary>
/// Web search configuration
/// </summary>
public class WebSearchConfiguration
{
    [Range(1, 10)]
    public int MaxConcurrentRequests { get; set; } = 3;

    [Range(100, 10000)]
    public int MinRequestIntervalMs { get; set; } = 1000;

    [Range(5, 120)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    [Range(0.0, 1.0)]
    public float MinCredibilityScore { get; set; } = 0.6f;

    [Required]
    public string SearchTermModel { get; set; } = "gpt-4o-mini";

    [Required]
    public string ValidationModel { get; set; } = "gpt-4o-mini";

    public List<TrustedSource> TrustedSources { get; set; } = new()
    {
        new TrustedSource
        {
            Name = "Motorcycle.com",
            BaseUrl = "https://www.motorcycle.com",
            SearchUrlTemplate = "https://www.motorcycle.com/search?q={query}",
            ContentSelector = "//article//p | //div[@class='content']//p",
            CredibilityScore = 0.9f
        },
        new TrustedSource
        {
            Name = "Cycle World",
            BaseUrl = "https://www.cycleworld.com",
            SearchUrlTemplate = "https://www.cycleworld.com/search?q={query}",
            ContentSelector = "//article//p | //div[@class='article-body']//p",
            CredibilityScore = 0.85f
        },
        new TrustedSource
        {
            Name = "RevZilla",
            BaseUrl = "https://www.revzilla.com",
            SearchUrlTemplate = "https://www.revzilla.com/search?query={query}",
            ContentSelector = "//div[@class='product-description']//p | //article//p",
            CredibilityScore = 0.8f
        }
    };
}

/// <summary>
/// Trusted web source configuration
/// </summary>
public class TrustedSource
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    public string SearchUrlTemplate { get; set; } = string.Empty;

    [Required]
    public string ContentSelector { get; set; } = string.Empty;

    [Range(0.0, 1.0)]
    public float CredibilityScore { get; set; } = 0.5f;
}

/// <summary>
/// Application Insights configuration
/// </summary>
public class TelemetryConfiguration
{
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    public bool EnableTelemetry { get; set; } = true;
    public bool EnablePerformanceCounters { get; set; } = true;
    public string ApplicationName { get; set; } = "MotorcycleRAG";
}