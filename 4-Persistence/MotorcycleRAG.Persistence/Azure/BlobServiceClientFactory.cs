using Azure.Identity;
using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Default implementation of <see cref="IBlobServiceClientFactory"/>. Constructs Azure
/// Storage Blobs SDK clients. Registered as a singleton in DI
/// (see <c>ServiceCollectionExtensions.AddAzureServices</c>).
/// </summary>
public class BlobServiceClientFactory : IBlobServiceClientFactory
{
    /// <inheritdoc />
    public BlobServiceClient Create(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new BlobServiceClient(connectionString);
    }

    /// <inheritdoc />
    public BlobServiceClient Create(TokenCredential credential, Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(endpoint);
        return new BlobServiceClient(endpoint, credential);
    }

    /// <summary>
    /// Resolves a <see cref="BlobServiceClient"/> from <see cref="BlobStorageOptions"/> using
    /// the supplied <paramref name="factory"/>.
    /// </summary>
    /// <remarks>
    /// Centralizes the environment-aware decision (Development connection string vs. managed
    /// identity endpoint) so that every consumer resolves the client identically. Consumers
    /// pass their injected <see cref="IBlobServiceClientFactory"/> so the SDK construction
    /// remains mockable in unit tests.
    /// </remarks>
    internal static BlobServiceClient CreateFromOptions(
        BlobStorageOptions options,
        IHostEnvironment environment,
        IBlobServiceClientFactory factory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(factory);

        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "BlobStorage:ConnectionString is allowed only in Development. " +
                    "Use BlobStorage:AccountEndpoint with DefaultAzureCredential outside Development.");
            }

            return factory.Create(options.ConnectionString);
        }

        if (string.IsNullOrWhiteSpace(options.AccountEndpoint))
        {
            throw new InvalidOperationException(
                "BlobStorage:AccountEndpoint is required. " +
                "Provide it through Azure App Configuration.");
        }

        return factory.Create(new DefaultAzureCredential(), new Uri(options.AccountEndpoint));
    }
}
