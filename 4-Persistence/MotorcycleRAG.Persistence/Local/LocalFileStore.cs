using MotorcycleRAG.Contracts.Interfaces;

namespace MotorcycleRAG.Persistence.Local;

/// <summary>
/// Local-disk implementation of the ingestion file-store contract.
/// </summary>
public sealed class LocalFileStore : ILocalFileStore
{
    /// <inheritdoc />
    public Task EnsureDirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(directoryPath);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task WriteAsync(string filePath, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(content);

        var directoryPath = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("The file path must include a directory.", nameof(filePath));
        }

        await EnsureDirectoryExistsAsync(directoryPath, cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<byte[]?> ReadAllBytesIfExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> DeleteIfExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            return Task.FromResult(false);
        }

        File.Delete(filePath);
        return Task.FromResult(true);
    }
}
