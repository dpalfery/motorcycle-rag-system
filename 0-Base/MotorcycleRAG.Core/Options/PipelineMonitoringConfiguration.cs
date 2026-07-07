namespace MotorcycleRAG.Core.Options;

public class PipelineMonitoringConfiguration {
    public bool EnableDetailedMetrics { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public int AlertThresholdFailures { get; set; } = 5;
}
