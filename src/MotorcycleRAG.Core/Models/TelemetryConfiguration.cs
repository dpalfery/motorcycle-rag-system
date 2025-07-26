using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

public class TelemetryConfiguration
{
    [Required] public string ConnectionString { get; set; } = string.Empty;
    public bool EnableTelemetry         { get; set; } = true;
    public bool EnablePerformanceCounters{ get; set; } = true;
    public string ApplicationName        { get; set; } = "MotorcycleRAG";
}