using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Hosting;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Creates Azure Blob Storage clients from validated application options.
/// </summary>
internal static class BlobServiceClientFactory
{
    public static BlobServiceClient Create(BlobStorageOptions options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "BlobStorage:ConnectionString is allowed only in Development. " +
                    "Use BlobStorage:AccountEndpoint with DefaultAzureCredential outside Development.");
            }

            return new BlobServiceClient(options.ConnectionString);
        }

        if (string.IsNullOrWhiteSpace(options.AccountEndpoint))
        {
            throw new InvalidOperationException(
                "BlobStorage:AccountEndpoint is required. " +
                "Provide it through Azure App Configuration.");
        }

        return new BlobServiceClient(
            new Uri(options.AccountEndpoint),
            new DefaultAzureCredential());
    }
}
