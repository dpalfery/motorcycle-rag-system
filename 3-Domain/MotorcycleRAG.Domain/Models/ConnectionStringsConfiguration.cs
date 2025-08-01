using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Wrapper for the top-level "ConnectionStrings" configuration section.
/// </summary>
public class ConnectionStringsConfiguration
{
    /// <summary>
    /// Application Insights connection string.
    /// </summary>
    [Required]
    public string ApplicationInsights { get; set; } = string.Empty;
}