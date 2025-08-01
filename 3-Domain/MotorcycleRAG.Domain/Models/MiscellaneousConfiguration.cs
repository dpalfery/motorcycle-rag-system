namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Miscellaneous top-level configuration values that do not yet warrant a dedicated options class.
/// </summary>
public class MiscellaneousConfiguration
{
    public string AllowedHosts { get; set; } = "*";
}