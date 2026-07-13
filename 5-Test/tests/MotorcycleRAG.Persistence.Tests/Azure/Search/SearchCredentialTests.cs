using MotorcycleRAG.Persistence.Azure.Search;

namespace MotorcycleRAG.Persistence.Tests.Azure.Search;

public sealed class SearchCredentialTests
{
    [Fact]
    public void Create_ShouldReturnNonNullTokenCredential()
    {
        var credential = SearchCredential.Create();

        credential.Should().NotBeNull();
    }

    [Fact]
    public void Create_ShouldReturnChainedTokenCredential()
    {
        var credential = SearchCredential.Create();

        credential.Should().BeOfType<global::Azure.Identity.ChainedTokenCredential>();
    }

    [Fact]
    public void Create_ShouldReturnSameTypeEachCall()
    {
        var credential1 = SearchCredential.Create();
        var credential2 = SearchCredential.Create();

        credential1.Should().BeOfType(credential2.GetType());
        credential2.Should().NotBeNull();
    }
}
