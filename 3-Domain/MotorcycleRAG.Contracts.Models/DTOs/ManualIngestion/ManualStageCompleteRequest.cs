namespace MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

public record ManualStageCompleteRequest(
    string? ArtifactPath,
    string? ArtifactHash,
    string? MetadataJson);
