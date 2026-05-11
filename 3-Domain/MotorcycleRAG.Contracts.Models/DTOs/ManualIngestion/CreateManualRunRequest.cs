namespace MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

public record CreateManualRunRequest(
    Guid DocumentId,
    string RunType,
    string? StartedFromStage,
    string? LocalWorkingFolder);
