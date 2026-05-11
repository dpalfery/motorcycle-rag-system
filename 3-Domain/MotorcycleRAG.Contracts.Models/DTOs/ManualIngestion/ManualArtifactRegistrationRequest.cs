namespace MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

public record ManualArtifactRegistrationRequest(
    string ArtifactType,
    string ArtifactPath,
    string? ArtifactHash,
    string? MetadataJson,
    int? ItemCount);
