namespace MotorcycleRAG.Contracts.Interfaces;

using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;


public interface IIndexedChunkRepository
{
    Task UpsertManyAsync(IReadOnlyCollection<IndexedChunkDto> chunks, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexedChunkDto>> GetByArtifactIdAsync(Guid artifactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all chunks for multiple artifact IDs in a single query.
    /// </summary>
    Task<IReadOnlyList<IndexedChunkDto>> GetByArtifactIdsAsync(
        IReadOnlyCollection<Guid> artifactIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexedChunkDto>> GetByIngestionJobIdAsync(Guid ingestionJobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexedChunkDto>> GetByUploadIdAsync(string uploadId, CancellationToken cancellationToken = default);
    Task DeleteByArtifactIdAsync(Guid artifactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all chunks for multiple artifact IDs in a single query.
    /// </summary>
    Task<int> DeleteByArtifactIdsAsync(
        IReadOnlyCollection<Guid> artifactIds,
        CancellationToken cancellationToken = default);

    Task DeleteByIngestionJobIdAsync(Guid ingestionJobId, CancellationToken cancellationToken = default);
    Task DeleteByUploadIdAsync(string uploadId, CancellationToken cancellationToken = default);
    Task<int> CountByArtifactIdAsync(Guid artifactId, CancellationToken cancellationToken = default);
    Task<int> CountByStatusAsync(Guid artifactId, string status, CancellationToken cancellationToken = default);
}
