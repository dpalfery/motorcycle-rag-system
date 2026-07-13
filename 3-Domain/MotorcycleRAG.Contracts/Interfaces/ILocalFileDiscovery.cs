namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Discovers local files for ingestion use cases.
/// </summary>
public interface ILocalFileDiscovery
{
    /// <summary>
    /// Returns the paths of files directly within a directory.
    /// </summary>
    Task<IReadOnlyList<string>> GetTopLevelFilePathsAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);
}
