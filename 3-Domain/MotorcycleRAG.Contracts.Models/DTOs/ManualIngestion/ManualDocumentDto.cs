namespace MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

public record ManualDocumentDto(
    Guid DocumentId,
    string SourceFileName,
    string CanonicalBlobPath,
    string? CanonicalBlobUri,
    string? ContentHash,
    string DocumentType,
    string? Make,
    string? Model,
    int? Year,
    DateTimeOffset UploadedAtUtc,
    ManualDocumentStatus CurrentStatus,
    string? CurrentStage,
    Guid? LastSuccessfulRunId,
    string? LastFailure);
