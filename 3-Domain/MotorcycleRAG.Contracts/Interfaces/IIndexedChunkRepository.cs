namespace MotorcycleRAG.Contracts.Interfaces;

using MotorcycleRAG.Domain.Entities;

public interface IIndexedChunkRepository
{
    Task UpsertManyAsync(IReadOnlyCollection<IndexedChunk> chunks, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexedChunk>> GetByArtifactIdAsync(Guid artifactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all chunks for multiple artifact IDs in a single query.
    /// </summary>
    Task<IReadOnlyList<IndexedChunk>> GetByArtifactIdsAsync(
        IReadOnlyCollection<Guid> artifactIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexedChunk>> GetByIngestionJobIdAsync(Guid ingestionJobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexedChunk>> GetByUploadIdAsync(string uploadId, CancellationToken cancellationToken = default);
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
