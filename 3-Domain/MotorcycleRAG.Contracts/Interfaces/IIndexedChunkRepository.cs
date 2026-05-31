namespace MotorcycleRAG.Contracts.Interfaces;

using MotorcycleRAG.Domain.Entities;

public interface IIndexedChunkRepository
{
    Task UpsertManyAsync(IReadOnlyCollection<IndexedChunk> chunks, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexedChunk>> GetByArtifactIdAsync(Guid artifactId, CancellationToken cancellationToken = default);
    Task DeleteByArtifactIdAsync(Guid artifactId, CancellationToken cancellationToken = default);
    Task<int> CountByArtifactIdAsync(Guid artifactId, CancellationToken cancellationToken = default);
    Task<int> CountByStatusAsync(Guid artifactId, string status, CancellationToken cancellationToken = default);
}
