namespace MotorcycleRAG.Contracts.Models.DTOs.Ingestion;

/// <summary>
/// Represents an orphaned search-chunks artifact that has no associated ingestion job.
/// A property-bag carrier with no domain invariant; used by the sweep service and later by
/// the admin listing endpoint (<c>GET /api/ingestion/artifacts/orphaned</c>, T17).
/// </summary>
public sealed record OrphanedArtifactDto(
    string UploadId,
    string Container,
    string BlobPath,
    string State,
    string? OrphanReason,
    int OrphanAttempts,
    DateTimeOffset? OrphanFirstDetectedUtc);
