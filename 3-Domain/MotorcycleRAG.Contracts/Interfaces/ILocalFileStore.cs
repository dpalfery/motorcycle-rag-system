namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Provides the local file operations required by ingestion use cases.
/// </summary>
public interface ILocalFileStore
{
    /// <summary>
    /// Ensures that a local directory exists.
    /// </summary>
    Task EnsureDirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a stream to a local file path.
    /// </summary>
    Task WriteAsync(string filePath, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all bytes from a local file when it exists.
    /// </summary>
    Task<byte[]?> ReadAllBytesIfExistsAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a local file when it exists.
    /// </summary>
    Task<bool> DeleteIfExistsAsync(string filePath, CancellationToken cancellationToken = default);
}
