using MotorcycleRAG.Contracts.Interfaces;

namespace MotorcycleRAG.Persistence.Local;

/// <summary>
/// Local-disk implementation of the ingestion file-discovery contract.
/// </summary>
public sealed class LocalFileDiscovery : ILocalFileDiscovery
{
    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetTopLevelFilePathsAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<string> filePaths = Directory.GetFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly);
        return Task.FromResult(filePaths);
    }
}
