namespace MotorcycleRAG.Application.Pipeline;

public class PipelineConfiguration
{
    public int MaxConcurrentProcessing { get; set; } = 5;
    public bool EnableAutoIndexing { get; set; } = true;
    public string ProcessingDirectory { get; set; } = "processing";
    public string ArchiveDirectory { get; set; } = "archive";
    public string ErrorDirectory { get; set; } = "error";
}

public class PipelineMonitoringConfiguration
{
    public bool EnableDetailedMetrics { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public int AlertThresholdFailures { get; set; } = 5;
}

