namespace MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

public record ManualStageFailRequest(
    string ErrorDetail,
    string? MetadataJson);
