using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.Azure.Blob;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IManualPageAssetStore"/>.
/// Stores per-page PNG images for motorcycle manuals under the key pattern:
/// <c>manuals/{manualId}/pages/{pageNumber}.png</c>
/// Uses DefaultAzureCredential in hosted environments and Development-only Azurite
/// connection strings when locally configured.
/// </summary>
public class BlobManualPageAssetStore : IManualPageAssetStore
{
    // TODO: Add ManualPagesContainerName to BlobStorageOptions if a different container
    // name is needed per-environment. Currently defaults to "manual-pages".
    private const string DefaultContainerName = "manual-pages";

    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<BlobManualPageAssetStore> _logger;

    public BlobManualPageAssetStore(
        IOptions<BlobStorageOptions> options,
        IHostEnvironment environment,
        ILogger<BlobManualPageAssetStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        var serviceClient = BlobServiceClientFactory.Create(options.Value, environment);

        _containerClient = serviceClient.GetBlobContainerClient(DefaultContainerName);
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> UploadPageAsync(
        Guid manualId,
        int pageNumber,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var blobKey = GetBlobKey(manualId, pageNumber);

        _logger.LogInformation("Uploading manual page blob {BlobKey}", blobKey);

        await _containerClient.CreateIfNotExistsAsync(
            PublicAccessType.None,
            cancellationToken: cancellationToken);

        var blobClient = _containerClient.GetBlobClient(blobKey);

        await blobClient.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "image/png" }
            },
            cancellationToken);

        _logger.LogInformation("Uploaded manual page blob {BlobKey} successfully", blobKey);

        return blobKey;
    }

    /// <inheritdoc/>
    public async Task<Stream> DownloadPageAsync(
        Guid manualId,
        int pageNumber,
        CancellationToken cancellationToken = default)
    {
        var blobKey = GetBlobKey(manualId, pageNumber);

        _logger.LogInformation("Downloading manual page blob {BlobKey}", blobKey);

        var blobClient = _containerClient.GetBlobClient(blobKey);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    /// <inheritdoc/>
    public async Task<bool> PageExistsAsync(
        Guid manualId,
        int pageNumber,
        CancellationToken cancellationToken = default)
    {
        var blobKey = GetBlobKey(manualId, pageNumber);
        var blobClient = _containerClient.GetBlobClient(blobKey);
        var response = await blobClient.ExistsAsync(cancellationToken);
        return response.Value;
    }

    /// <inheritdoc/>
    public async Task<string?> GetETagAsync(
        Guid manualId,
        int pageNumber,
        CancellationToken cancellationToken = default)
    {
        var blobKey = GetBlobKey(manualId, pageNumber);
        var blobClient = _containerClient.GetBlobClient(blobKey);

        try
        {
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            return properties.Value.ETag.ToString();
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            return null;
        }
    }

    private static string GetBlobKey(Guid manualId, int pageNumber)
        => $"manuals/{manualId}/pages/{pageNumber}.png";
}
