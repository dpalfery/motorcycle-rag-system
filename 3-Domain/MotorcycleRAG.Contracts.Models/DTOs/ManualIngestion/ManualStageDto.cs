namespace MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

public record ManualStageDto(
    Guid StageId,
    Guid RunId,
    string StageName,
    ManualStageStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ArtifactPath,
    string? ArtifactHash,
    string? MetadataJson,
    string? ErrorDetail);
