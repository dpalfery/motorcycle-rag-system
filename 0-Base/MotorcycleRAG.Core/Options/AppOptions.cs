using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Aggregated application configuration that maps to the root of the configuration hierarchy (appsettings / Azure App Configuration).
/// When <see cref="UseAzureAppOptions"/> is enabled with a sentinel, the bound instance will be refreshed at runtime via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>.
/// </summary>
public class AppOptions
{
    /// <summary>
    /// Connection strings (only Application Insights is currently required but the object leaves room for future values).
    /// </summary>
    public ConnectionStringsOptions ConnectionStrings { get; set; } = new();

    /// <summary>
    /// Azure Foundry / Cognitive Services configuration.
    /// </summary>
    [Required]
    public AzureFoundryOptions AzureAI { get; set; } = new();

    /// <summary>
    /// Search service configuration.
    /// </summary>
    [Required]
    public SearchOptions Search { get; set; } = new();

    /// <summary>
    /// Application Insights / telemetry configuration.
    /// </summary>
    [Required]
    public TelemetryOptions ApplicationInsights { get; set; } = new();

    /// <summary>
    /// Resilience strategy configuration.
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// Optional web-search behaviour configuration.
    /// </summary>
    public WebSearchOptions? WebSearch { get; set; }

    /// <summary>
    /// Miscellaneous top-level settings that don’t fit into a dedicated object.
    /// </summary>
    public MiscellaneousOptions Miscellaneous { get; set; } = new();
}
