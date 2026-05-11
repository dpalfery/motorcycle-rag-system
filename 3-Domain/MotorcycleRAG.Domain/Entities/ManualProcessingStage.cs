using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domain.Entities;

public class ManualProcessingStage
{
    public Guid StageId { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public string StageName { get; set; } = string.Empty;
    public ManualStageStatus Status { get; set; } = ManualStageStatus.Started;
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ArtifactPath { get; set; }
    public string? ArtifactHash { get; set; }
    public string? MetadataJson { get; set; }
    public string? ErrorDetail { get; set; }
}
