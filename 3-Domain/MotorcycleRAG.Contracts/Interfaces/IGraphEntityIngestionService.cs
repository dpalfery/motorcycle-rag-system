namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Reads extracted graph entities from blob storage and upserts them into the graph database.
/// </summary>
public interface IGraphEntityIngestionService {
    /// <summary>
    /// Reads graph entities (nodes and edges) from blob storage for the specified upload
    /// and upserts them into the SQL Server Graph database.
    /// </summary>
    /// <param name="uploadId">The upload batch identifier used to locate the entities blob.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task IngestAsync(string uploadId, CancellationToken cancellationToken = default);
}
