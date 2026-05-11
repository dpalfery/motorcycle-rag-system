namespace MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

public record UnifiedOperationDto(
    string OperationId,
    string OperationType,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? TargetIdentity, // DocumentId or FileName
    string? CurrentStage,
    string? ErrorSummary);
