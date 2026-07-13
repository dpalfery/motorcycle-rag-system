using Azure.Core;
using Azure.Identity;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Centralizes and caches Azure SDK credential policies for Persistence integrations.
/// </summary>
public sealed class AzureCredentialProvider : IAzureCredentialProvider
{
    private readonly Lazy<TokenCredential> _defaultCredential = new(CreateDefaultCredential);
    private readonly Lazy<TokenCredential> _searchCredential = new(CreateSearchCredential);
    private readonly object _graphCredentialLock = new();
    private TokenCredential? _graphCredential;

    /// <inheritdoc />
    public TokenCredential GetDefaultCredential() => _defaultCredential.Value;

    /// <inheritdoc />
    public TokenCredential GetSearchCredential() => _searchCredential.Value;

    /// <inheritdoc />
    public TokenCredential GetGraphCredential(ExternalIdentityProvisioningOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (_graphCredentialLock)
        {
            return _graphCredential ??= CreateGraphCredential(options);
        }
    }

    private static TokenCredential CreateDefaultCredential() => new DefaultAzureCredential();

    private static TokenCredential CreateSearchCredential() => new ChainedTokenCredential(
        new ManagedIdentityCredential(new ManagedIdentityCredentialOptions()),
        new AzureCliCredential());

    private static TokenCredential CreateGraphCredential(ExternalIdentityProvisioningOptions options)
    {
        var credentials = new List<TokenCredential>
        {
            string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
                ? new ManagedIdentityCredential(new ManagedIdentityCredentialOptions())
                : new ManagedIdentityCredential(
                    ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId.Trim()))
        };

        if (!string.IsNullOrWhiteSpace(options.TenantId)
            && !string.IsNullOrWhiteSpace(options.ClientId)
            && !string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            credentials.Add(new ClientSecretCredential(
                options.TenantId.Trim(),
                options.ClientId.Trim(),
                options.ClientSecret.Trim()));
        }

        return credentials.Count == 1 ? credentials[0] : new ChainedTokenCredential([.. credentials]);
    }
}
