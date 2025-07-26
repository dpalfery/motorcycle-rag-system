using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Aggregated application configuration that maps to the root of the configuration hierarchy (appsettings / Azure App Configuration).
/// When <see cref="UseAzureAppConfiguration"/> is enabled with a sentinel, the bound instance will be refreshed at runtime via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>.
/// </summary>
public class AppConfiguration
{
    /// <summary>
    /// Connection strings (only Application Insights is currently required but the object leaves room for future values).
    /// </summary>
    public ConnectionStringsConfiguration ConnectionStrings { get; set; } = new();

    /// <summary>
    /// Azure AI / Cognitive Services configuration.
    /// </summary>
    [Required]
    public AzureAIConfiguration AzureAI { get; set; } = new();

    /// <summary>
    /// Search service configuration.
    /// </summary>
    [Required]
    public SearchConfiguration Search { get; set; } = new();

    /// <summary>
    /// Application Insights / telemetry configuration.
    /// </summary>
    [Required]
    public TelemetryConfiguration ApplicationInsights { get; set; } = new();

    /// <summary>
    /// Resilience strategy configuration.
    /// </summary>
    public ResilienceConfiguration Resilience { get; set; } = new();

    /// <summary>
    /// Optional web-search behaviour configuration.
    /// </summary>
    public WebSearchConfiguration? WebSearch { get; set; }

    /// <summary>
    /// Miscellaneous top-level settings that don’t fit into a dedicated object.
    /// </summary>
    public MiscellaneousConfiguration Miscellaneous { get; set; } = new();
}

/// <summary>
/// Wrapper for the top-level ConnectionStrings section.
/// </summary>
public class ConnectionStringsConfiguration
{
    public string ApplicationInsights { get; set; } = string.Empty;
}

/// <summary>
/// Wrapper for any top-level simple settings that have not yet been assigned a dedicated model.
/// </summary>
public class MiscellaneousConfiguration
{
    public string AllowedHosts { get; set; } = "*";
}