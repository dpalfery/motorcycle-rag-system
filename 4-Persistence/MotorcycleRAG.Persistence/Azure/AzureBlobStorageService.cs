using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IBlobStorageService"/>.
/// Uses the injected <see cref="IAzureCredentialProvider"/> against
/// <see cref="BlobStorageOptions.AccountEndpoint"/>.
/// In Development only, a local Azurite connection string can be supplied through
/// developer-only configuration.
/// </summary>
public class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(
        IOptions<BlobStorageOptions> options,
        IHostEnvironment environment,
        IBlobServiceClientFactory blobServiceClientFactory,
        IAzureCredentialProvider credentialProvider,
        ILogger<AzureBlobStorageService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(blobServiceClientFactory);
        ArgumentNullException.ThrowIfNull(credentialProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _blobServiceClient = BlobServiceClientFactory.CreateFromOptions(
            options.Value, environment, blobServiceClientFactory, credentialProvider);
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> UploadAsync(
        string containerName,
        string blobName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        ArgumentNullException.ThrowIfNull(content);

        _logger.LogInformation("Uploading blob {BlobName} to container {Container}",
            LogSanitizer.Sanitize(blobName), containerName);  // codeql[cs/log-forging]

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(
            PublicAccessType.None,
            cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(blobName);

        await blobClient.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            },
            cancellationToken);

        _logger.LogInformation("Blob {BlobName} uploaded successfully to {Container}",
            LogSanitizer.Sanitize(blobName), containerName);  // codeql[cs/log-forging]

        return blobClient.Uri.ToString();
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.ExistsAsync(cancellationToken);
        return response.Value;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BlobObjectDescriptor>> ListAsync(
        string containerName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobs = new List<BlobObjectDescriptor>();

        try
        {
            var options = new GetBlobsOptions { Traits = BlobTraits.Metadata };
            await foreach (var blob in containerClient.GetBlobsAsync(options, cancellationToken: cancellationToken))
            {
                var metadata = blob.Metadata == null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(blob.Metadata);

                blobs.Add(new BlobObjectDescriptor
                {
                    Name = blob.Name,
                    ContentType = blob.Properties.ContentType,
                    SizeBytes = blob.Properties.ContentLength ?? 0L,
                    LastModifiedUtc = blob.Properties.LastModified,
                    Metadata = metadata
                });
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation(
                "Blob container {Container} was not found while listing pending files.",
                containerName);
            return Array.Empty<BlobObjectDescriptor>();
        }

        return blobs;
    }

    /// <inheritdoc/>
    public async Task<Stream> DownloadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);

        _logger.LogInformation("Downloading blob {BlobName} from container {Container}",
            LogSanitizer.Sanitize(blobName), containerName);  // codeql[cs/log-forging]

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteIfExistsAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);

        _logger.LogInformation(
            "Deleting blob {BlobName} from container {Container} when present.",
            blobName,
            containerName);

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: cancellationToken);

        return response.Value;
    }

    /// <inheritdoc/>
    public async Task SetMetadataAsync(
        string containerName,
        string blobName,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        ArgumentNullException.ThrowIfNull(metadata);

        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);
            await blobClient.SetMetadataAsync(metadata, cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Set metadata on blob {BlobName} in container {Container}.",
                LogSanitizer.Sanitize(blobName),  // codeql[cs/log-forging]
                containerName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to set metadata on blob {BlobName} in container {Container}. This is best-effort only.",
                LogSanitizer.Sanitize(blobName),  // codeql[cs/log-forging]
                containerName);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> GetMetadataAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);

        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

            return properties.Value.Metadata == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(properties.Value.Metadata);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation(
                "Blob {BlobName} was not found in container {Container} when retrieving metadata.",
                LogSanitizer.Sanitize(blobName),  // codeql[cs/log-forging]
                containerName);
            return new Dictionary<string, string>();
        }
    }
}
