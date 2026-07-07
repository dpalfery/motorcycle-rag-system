namespace MotorcycleRAG.Contracts.Interfaces;

using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

public interface IIndexedArtifactRepository
{
    Task<IndexedArtifact> UpsertAsync(IndexedArtifact artifact, CancellationToken cancellationToken = default);
    Task<IndexedArtifact?> GetByIdAsync(Guid artifactId, CancellationToken cancellationToken = default);
    Task<IndexedArtifact?> GetByUploadAndTypeAsync(string uploadId, string artifactType, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexedArtifact>> GetByStatesAsync(IReadOnlyCollection<IndexedArtifactState> states, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexedArtifact>> GetAllAsync(int maxCount = 1000, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexedArtifact>> GetByIngestionJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexedArtifact>> GetByUploadIdAsync(string uploadId, CancellationToken cancellationToken = default);
    Task DeleteByIdAsync(Guid artifactId, CancellationToken cancellationToken = default);
}
