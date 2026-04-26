using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IBlobStorageService"/>.
/// Uses DefaultAzureCredential (Managed Identity in Azure, developer credential locally).
/// Connection string must NEVER be stored in code or config files.
/// Endpoint is read from <see cref="BlobStorageOptions.AccountEndpoint"/> which
/// should be provided through Azure App Configuration.
/// </summary>
public class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(
        IOptions<BlobStorageOptions> options,
        ILogger<AzureBlobStorageService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var opts = options.Value;

        if (string.IsNullOrWhiteSpace(opts.AccountEndpoint))
            throw new InvalidOperationException(
                "BlobStorage:AccountEndpoint is required. " +
                "Provide it through Azure App Configuration.");

        _blobServiceClient = new BlobServiceClient(
            new Uri(opts.AccountEndpoint),
            new DefaultAzureCredential());

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
            LogSanitizer.Sanitize(blobName), LogSanitizer.Sanitize(containerName));

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
            LogSanitizer.Sanitize(blobName), LogSanitizer.Sanitize(containerName));

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
            await foreach (var blob in containerClient.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                blobs.Add(new BlobObjectDescriptor
                {
                    Name = blob.Name,
                    ContentType = blob.Properties.ContentType,
                    SizeBytes = blob.Properties.ContentLength ?? 0L,
                    LastModifiedUtc = blob.Properties.LastModified
                });
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation(
                "Blob container {Container} was not found while listing pending files.",
                LogSanitizer.Sanitize(containerName));
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
            LogSanitizer.Sanitize(blobName), LogSanitizer.Sanitize(containerName));

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }
}
