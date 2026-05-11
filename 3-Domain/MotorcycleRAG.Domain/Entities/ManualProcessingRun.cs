using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domain.Entities;

public class ManualProcessingRun
{
    public Guid RunId { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string RunType { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public ManualRunStatus Status { get; set; } = ManualRunStatus.Started;
    public string? StartedFromStage { get; set; }
    public string? CompletedStage { get; set; }
    public string? LocalWorkingFolder { get; set; }
    public string? ProcessorHost { get; set; }
    public string? ErrorSummary { get; set; }
    public int? ChunkCount { get; set; }
    public int? GraphEntityCount { get; set; }
    public int? GraphRelationCount { get; set; }
    public int? VectorCount { get; set; }
}
