namespace MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

public sealed class ManualStageDto
{
    public ManualStageDto() { }

    public ManualStageDto(Guid stageId, Guid runId, string stageName, ManualStageStatus status,
        DateTimeOffset startedAtUtc, DateTimeOffset? completedAtUtc, string? artifactPath,
        string? artifactHash, string? metadataJson, string? errorDetail)
    {
        StageId = stageId; RunId = runId; StageName = stageName; Status = status;
        StartedAtUtc = startedAtUtc; CompletedAtUtc = completedAtUtc; ArtifactPath = artifactPath;
        ArtifactHash = artifactHash; MetadataJson = metadataJson; ErrorDetail = errorDetail;
    }

    public Guid StageId { get; set; }
    public Guid RunId { get; set; }
    public string StageName { get; set; } = string.Empty;
    public ManualStageStatus Status { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ArtifactPath { get; set; }
    public string? ArtifactHash { get; set; }
    public string? MetadataJson { get; set; }
    public string? ErrorDetail { get; set; }
}
