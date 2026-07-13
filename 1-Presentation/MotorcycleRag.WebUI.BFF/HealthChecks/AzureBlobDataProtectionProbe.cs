using Azure.Storage.Blobs;

namespace MotorcycleRag.WebUI.BFF.HealthChecks;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IDataProtectionBlobProbe"/>.
/// </summary>
public sealed class AzureBlobDataProtectionProbe : IDataProtectionBlobProbe
{
    private readonly BlobClient _blobClient;

    /// <summary>Initializes a probe using the blob client configured for Data Protection key persistence.</summary>
    public AzureBlobDataProtectionProbe(BlobClient blobClient)
    {
        _blobClient = blobClient ?? throw new ArgumentNullException(nameof(blobClient));
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(CancellationToken cancellationToken)
    {
        return (await _blobClient.ExistsAsync(cancellationToken).ConfigureAwait(false)).Value;
    }
}
