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
}
