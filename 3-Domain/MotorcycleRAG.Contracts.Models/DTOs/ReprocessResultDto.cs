namespace MotorcycleRAG.Contracts.Models.DTOs;

public record ReprocessResultDto(
    int ArtifactsProcessed,
    int ArtifactsSucceeded,
    int ArtifactsPartiallyIndexed,
    int ArtifactsFailed);
