namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Abstraction for storing and retrieving per-page viewable assets for motorcycle manuals.
/// Blob key pattern: manuals/{manualId}/pages/{pageNumber}.png
/// Implementations must use DefaultAzureCredential (no connection strings).
/// </summary>
public interface IManualPageAssetStore
{
    /// <summary>
    /// Uploads a page image asset to blob storage.
    /// </summary>
    /// <param name="manualId">The manual document GUID.</param>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="content">Image stream (PNG).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The blob key used for storage.</returns>
    Task<string> UploadPageAsync(
        Guid manualId,
        int pageNumber,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a page image asset from blob storage.
    /// </summary>
    /// <param name="manualId">The manual document GUID.</param>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream of the image data.</returns>
    Task<Stream> DownloadPageAsync(
        Guid manualId,
        int pageNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a page asset exists in blob storage.
    /// </summary>
    Task<bool> PageExistsAsync(
        Guid manualId,
        int pageNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the ETag for a page asset (used for HTTP caching headers).
    /// Returns null if the asset does not exist.
    /// </summary>
    Task<string?> GetETagAsync(
        Guid manualId,
        int pageNumber,
        CancellationToken cancellationToken = default);
}
