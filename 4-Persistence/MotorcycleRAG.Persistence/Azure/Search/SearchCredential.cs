using Azure.Core;
using Azure.Identity;

namespace MotorcycleRAG.Persistence.Azure.Search;

/// <summary>
/// Builds the <see cref="TokenCredential"/> used by every Azure AI Search client.
/// </summary>
/// <remarks>
/// The API Container App is provisioned with a combined system-assigned + user-assigned
/// identity (<c>7-Deployment/infrastructure/Program.cs</c>): the system-assigned identity holds
/// "Search Index Data Contributor" (<c>Program.cs:889-899</c>), while the user-assigned identity
/// exists only for ACR pulls and has no Search RBAC. <see cref="DefaultAzureCredential"/>'s long
/// credential chain does not guarantee which of the two managed identities its
/// <see cref="ManagedIdentityCredential"/> leg resolves to on a resource carrying both types.
/// An unqualified <see cref="ManagedIdentityCredential"/> reliably targets the system-assigned
/// identity — the same construction <c>AppConfigurationExtensions.cs</c> already uses
/// successfully against App Configuration/Key Vault in this deployment — so Search clients use
/// that explicitly instead, falling back to <see cref="AzureCliCredential"/> for local
/// development where no managed identity exists.
/// </remarks>
internal static class SearchCredential {
    public static TokenCredential Create() =>
        new ChainedTokenCredential(
            new ManagedIdentityCredential(new ManagedIdentityCredentialOptions()),
            new AzureCliCredential());
}
