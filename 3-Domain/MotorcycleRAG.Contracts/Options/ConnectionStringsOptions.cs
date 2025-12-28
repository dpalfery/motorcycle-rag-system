using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Options;

/// <summary>
/// Wrapper for the top-level "ConnectionStrings" configuration section.
/// </summary>
public class ConnectionStringsOptions
{
    /// <summary>
    /// Application Insights connection string.
    /// </summary>
    [Required]
    public string ApplicationInsights { get; set; } = string.Empty;
}
