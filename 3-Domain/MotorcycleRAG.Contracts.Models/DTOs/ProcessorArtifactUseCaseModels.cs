namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>Input required to store a local-processor artifact.</summary>
public sealed record ProcessorArtifactUploadRequest(
    string UploadId,
    string ArtifactType,
    Stream Content,
    string ContentType);

/// <summary>Outcome of a processor artifact upload use case.</summary>
public sealed record ProcessorArtifactUploadResult(
    ProcessorArtifactUploadResponse? Response,
    ProcessorArtifactOperationStatus Status);

/// <summary>Outcome of a processor source-download use case.</summary>
public sealed record ProcessorArtifactSourceResult(
    Stream? Content,
    string? ContentType,
    ProcessorArtifactOperationStatus Status);

/// <summary>Processor artifact outcomes that the HTTP adapter maps to response status codes.</summary>
public enum ProcessorArtifactOperationStatus
{
    Success,
    InvalidUploadId,
    InvalidDocumentType,
    InvalidArtifactType,
    Unauthorized,
    NotFound,
    IndexingSkipped
}
