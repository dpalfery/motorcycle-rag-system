namespace MotorcycleRAG.Contracts.Interfaces;

using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;

using MotorcycleRAG.Domain.Enums;

public interface IIndexedArtifactRepository
{
    Task<IndexedArtifactDto> UpsertAsync(IndexedArtifactDto artifact, CancellationToken cancellationToken = default);
    Task<IndexedArtifactDto?> GetByIdAsync(Guid artifactId, CancellationToken cancellationToken = default);
    Task<IndexedArtifactDto?> GetByUploadAndTypeAsync(string uploadId, string artifactType, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexedArtifactDto>> GetByStatesAsync(IReadOnlyCollection<IndexedArtifactState> states, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexedArtifactDto>> GetAllAsync(int maxCount = 1000, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexedArtifactDto>> GetByIngestionJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexedArtifactDto>> GetByUploadIdAsync(string uploadId, CancellationToken cancellationToken = default);
    Task DeleteByIdAsync(Guid artifactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes multiple artifacts by their IDs in a single query.
    /// </summary>
    Task<int> DeleteByIdsAsync(
        IReadOnlyCollection<Guid> artifactIds,
        CancellationToken cancellationToken = default);
}
