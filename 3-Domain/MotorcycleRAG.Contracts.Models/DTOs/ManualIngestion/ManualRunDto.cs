namespace MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

public record ManualRunDto(
    Guid RunId,
    Guid DocumentId,
    string RunType,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    ManualRunStatus Status,
    string? StartedFromStage,
    string? CompletedStage,
    string? LocalWorkingFolder,
    string? ProcessorHost,
    string? ErrorSummary,
    int? ChunkCount,
    int? GraphEntityCount,
    int? GraphRelationCount,
    int? VectorCount);
