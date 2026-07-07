namespace MotorcycleRAG.Core.Options;

public class PipelineConfiguration {
    public int MaxConcurrentProcessing { get; set; } = 5;
    public bool EnableAutoIndexing { get; set; } = true;
    public string ProcessingDirectory { get; set; } = "processing";
    public string ArchiveDirectory { get; set; } = "archive";
    public string ErrorDirectory { get; set; } = "error";
}
