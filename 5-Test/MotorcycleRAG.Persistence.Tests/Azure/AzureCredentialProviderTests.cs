using global::Azure.Identity;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Azure;

namespace MotorcycleRAG.Persistence.Tests.Azure;

public sealed class AzureCredentialProviderTests
{
    [Fact]
    public void GetDefaultCredential_ShouldReuseDefaultCredential()
    {
        var provider = new AzureCredentialProvider();

        var first = provider.GetDefaultCredential();
        var second = provider.GetDefaultCredential();

        first.Should().BeOfType<DefaultAzureCredential>();
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void GetSearchCredential_ShouldReuseManagedIdentityFirstPolicy()
    {
        var provider = new AzureCredentialProvider();

        var first = provider.GetSearchCredential();
        var second = provider.GetSearchCredential();

        first.Should().BeOfType<ChainedTokenCredential>();
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void GetGraphCredential_WhenClientSecretIsConfigured_ShouldReuseGraphPolicy()
    {
        var provider = new AzureCredentialProvider();
        var options = new ExternalIdentityProvisioningOptions
        {
            TenantId = "tenant-id",
            ClientId = "client-id",
            ClientSecret = "test-only-secret"
        };

        var first = provider.GetGraphCredential(options);
        var second = provider.GetGraphCredential(options);

        first.Should().BeOfType<ChainedTokenCredential>();
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void GetGraphCredential_WhenOptionsIsNull_ShouldThrowArgumentNullException()
    {
        var provider = new AzureCredentialProvider();

        var act = () => provider.GetGraphCredential(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void GetGraphCredential_WhenNoClientSecretConfigured_ShouldReturnSingleManagedIdentity()
    {
        var provider = new AzureCredentialProvider();
        var options = new ExternalIdentityProvisioningOptions
        {
            // No TenantId/ClientId/ClientSecret — only ManagedIdentity path
        };

        var credential = provider.GetGraphCredential(options);

        // When only ManagedIdentity is configured (no client secret),
        // the returned credential is a single ManagedIdentityCredential, not ChainedTokenCredential.
        credential.Should().BeOfType<ManagedIdentityCredential>();
    }

    [Fact]
    public void GetGraphCredential_WhenManagedIdentityClientIdIsSet_ShouldReturnManagedIdentityCredential()
    {
        var provider = new AzureCredentialProvider();
        var options = new ExternalIdentityProvisioningOptions
        {
            ManagedIdentityClientId = "user-assigned-client-id"
        };

        var credential = provider.GetGraphCredential(options);

        credential.Should().BeOfType<ManagedIdentityCredential>();
    }
}
