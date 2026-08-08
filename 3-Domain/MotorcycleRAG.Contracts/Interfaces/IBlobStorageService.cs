using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Contract for uploading files to Azure Blob Storage.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Uploads a stream to the specified container and blob name.
    /// Returns the fully-qualified blob URI.
    /// </summary>
    Task<string> UploadAsync(
        string containerName,
        string blobName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists blobs in the specified container.
    /// </summary>
    Task<IReadOnlyList<BlobObjectDescriptor>> ListAsync(
        string containerName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a blob with the given name exists in the container.
    /// </summary>
    Task<bool> ExistsAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a blob as a stream. Caller is responsible for disposing.
    /// </summary>
    Task<Stream> DownloadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blob when it exists.
    /// </summary>
    Task<bool> DeleteIfExistsAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets custom metadata on a blob. Best-effort only — errors are logged but not thrown.
    /// </summary>
    Task SetMetadataAsync(
        string containerName,
        string blobName,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the metadata for a blob. Returns an empty dictionary for a missing blob rather than throwing.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetMetadataAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default);
}
