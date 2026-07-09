using Azure.Core;
using Azure.Identity;
using MotorcycleRAG.Persistence.Azure.Search;

namespace MotorcycleRAG.UnitTests.Azure;

/// <summary>
/// Covers <see cref="SearchCredential"/>, the pinned credential used by every Azure AI Search
/// client instead of a bare <see cref="DefaultAzureCredential"/> (see
/// 6-Docs/operations/azure-search-rbac-local-dev.md for why the API Container App's combined
/// system-assigned/user-assigned identity made <c>DefaultAzureCredential</c> unreliable here).
/// </summary>
public class SearchCredentialTests {
    [Fact]
    public void Create_ReturnsATokenCredential() {
        var credential = SearchCredential.Create();

        credential.Should().NotBeNull();
        credential.Should().BeAssignableTo<TokenCredential>();
    }

    [Fact]
    public void Create_ReturnsAChainedTokenCredential_NotABareDefaultAzureCredential() {
        var credential = SearchCredential.Create();

        credential.Should().BeOfType<ChainedTokenCredential>(
            "Search clients must not fall back to DefaultAzureCredential's full chain, " +
            "which cannot reliably disambiguate the API Container App's two managed identities");
    }

    [Fact]
    public void Create_ReturnsANewInstanceEachCall() {
        var first = SearchCredential.Create();
        var second = SearchCredential.Create();

        first.Should().NotBeSameAs(second, "callers are responsible for caching/reusing a single instance");
    }
}
