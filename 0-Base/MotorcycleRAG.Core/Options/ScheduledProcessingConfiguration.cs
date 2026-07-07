namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Configuration for scheduled processing.
/// </summary>
public class ScheduledProcessingConfiguration {
    public string DefaultCronExpression { get; set; } = "0 2 * * *";
    public bool IsEnabledByDefault { get; set; } = true;
    public TimeSpan DefaultProcessingWindow { get; set; } = TimeSpan.FromHours(4);
    public int DefaultMaxConcurrentJobs { get; set; } = 3;
    public string BaseDirectory { get; set; } = "data";
}
