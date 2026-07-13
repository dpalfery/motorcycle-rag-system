using Azure.Core;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Supplies cached Azure authentication policies to Persistence-owned SDK client factories
/// and adapters.
/// </summary>
/// <remarks>
/// This interface remains in Persistence because it returns Azure SDK types. Application and
/// Contracts consumers continue to depend only on their existing service contracts.
/// </remarks>
public interface IAzureCredentialProvider
{
    /// <summary>Gets the shared default Azure credential used by Foundry and Blob Storage.</summary>
    TokenCredential GetDefaultCredential();

    /// <summary>Gets the Search-specific credential policy.</summary>
    TokenCredential GetSearchCredential();

    /// <summary>Gets the configured Microsoft Graph credential policy.</summary>
    TokenCredential GetGraphCredential(ExternalIdentityProvisioningOptions options);
}
