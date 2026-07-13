using Azure.Core;
using Azure.Storage.Blobs;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Factory abstraction for creating Azure Blob Storage SDK clients.
/// </summary>
/// <remarks>
/// <para>
/// This interface lives in <b>Persistence</b> (not Contracts) because it returns the
/// concrete <see cref="BlobServiceClient"/> Azure SDK type. Contracts is forbidden from
/// referencing any infrastructure SDK (EF Core, Azure, SQL, HTTP), and adding the Azure
/// Storage Blobs package to Contracts would violate the Dependency Rule. Every
/// BlobServiceClient consumer lives in Persistence, so the abstraction is consumed entirely
/// within this layer.
/// </para>
/// <para>
/// Extracted from sealed SDK client construction (previously a static helper called directly
/// inside service constructors) so that consumers can be unit tested without instantiating
/// real <see cref="BlobServiceClient"/> instances. The environment/option resolution logic
/// stays with each consumer; this factory only constructs the SDK client.
/// </para>
/// </remarks>
public interface IBlobServiceClientFactory
{
    /// <summary>
    /// Creates a <see cref="BlobServiceClient"/> authenticated via connection string.
    /// Typically used only in Development (Azurite).
    /// </summary>
    BlobServiceClient Create(string connectionString);

    /// <summary>
    /// Creates a <see cref="BlobServiceClient"/> authenticated via a token credential
    /// against the supplied account <paramref name="endpoint"/>.
    /// </summary>
    BlobServiceClient Create(TokenCredential credential, Uri endpoint);
}
