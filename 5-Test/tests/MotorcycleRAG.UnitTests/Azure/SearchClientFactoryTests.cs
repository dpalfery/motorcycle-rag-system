using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.ValueObjects;
using MotorcycleRAG.Persistence.Azure.Search;

namespace MotorcycleRAG.UnitTests.Azure;

/// <summary>
/// Unit tests for the per-index <see cref="SearchClientFactory"/> (D4 category partitioning).
/// Proves that each category resolves to its own index name and its own cached
/// <see cref="Azure.Search.Documents.SearchClient"/>, and that no singleton client bound
/// to a single index is produced.
/// </summary>
public class SearchClientFactoryTests
{
    private static SearchClientFactory CreateFactory(Mock<SearchIndexClient>? indexClientMock = null)
    {
        var options = Options.Create(new AzureFoundryOptions
        {
            SearchServiceEndpoint = "https://mcr-rag-dev-wcus-search.search.windows.net/"
        });
        indexClientMock ??= new Mock<SearchIndexClient>(new Uri("https://mcr-rag-dev-wcus-search.search.windows.net/"), new global::Azure.Identity.DefaultAzureCredential());
        return new SearchClientFactory(options, indexClientMock.Object);
    }

    [Theory]
    [InlineData(nameof(MotorcycleCategory.Dirt), "motorcycle-dirt")]
    [InlineData(nameof(MotorcycleCategory.Touring), "motorcycle-touring")]
    [InlineData(nameof(MotorcycleCategory.Sport), "motorcycle-sport")]
    [InlineData(nameof(MotorcycleCategory.Cruiser), "motorcycle-cruiser")]
    public void GetIndexName_MapsEachCategoryToItsPartition(string categoryName, string expectedIndex)
    {
        var factory = CreateFactory();
        var category = MotorcycleCategory.Parse(categoryName.ToLowerInvariant());

        factory.GetIndexName(category).Should().Be(expectedIndex);
    }

    [Fact]
    public void GetClient_ReturnsNonNullClientPerCategory()
    {
        var factory = CreateFactory();

        foreach (var category in MotorcycleCategory.All)
        {
            factory.GetClient(category).Should().NotBeNull(
                "category {0} must resolve to a client", category.Value);
        }
    }

    [Fact]
    public void GetClient_CachesPerCategory_SameInstanceForSameCategory()
    {
        var factory = CreateFactory();

        var first = factory.GetClient(MotorcycleCategory.Dirt);
        var second = factory.GetClient(MotorcycleCategory.Dirt);

        first.Should().BeSameAs(second, "a category's client must be cached, not rebuilt per call");
    }

    [Fact]
    public void GetClient_DistinctInstancesAcrossCategories()
    {
        var factory = CreateFactory();

        var dirt = factory.GetClient(MotorcycleCategory.Dirt);
        var sport = factory.GetClient(MotorcycleCategory.Sport);

        dirt.Should().NotBeSameAs(sport, "each category partition must have its own client");
    }

    [Fact]
    public void DefaultCategory_IsSport_MatchingClassifierFallback()
    {
        CreateFactory().DefaultCategory.Should().Be(MotorcycleCategory.Sport);
    }

    [Fact]
    public void GetDefaultClient_EqualsClientForDefaultCategory()
    {
        var factory = CreateFactory();

        factory.GetDefaultClient().Should().BeSameAs(factory.GetClient(factory.DefaultCategory));
    }

    [Fact]
    public void GetClient_UndefinedCategory_NormalizesToDefault()
    {
        var factory = CreateFactory();

        // An uninitialized (default) value object must not target a bare "motorcycle-" index.
        factory.GetClient(default).Should().BeSameAs(factory.GetClient(factory.DefaultCategory));
        factory.GetIndexName(default).Should().Be("motorcycle-sport");
    }

    [Fact]
    public void AllCategories_ContainsExactlyFourPartitions()
    {
        CreateFactory().AllCategories.Should().HaveCount(4);
    }

    [Fact]
    public void Constructor_RejectsInvalidEndpoint()
    {
        var indexClientMock = new Mock<SearchIndexClient>(new Uri("https://mcr-rag-dev-wcus-search.search.windows.net/"), new global::Azure.Identity.DefaultAzureCredential());

        var act = () => new SearchClientFactory(Options.Create(new AzureFoundryOptions
        {
            SearchServiceEndpoint = "not-a-uri"
        }), indexClientMock.Object);

        act.Should().Throw<ArgumentException>();
    }

    // ----------------------------------------------------------------
    // T6 index-existence precheck: IndexExistsAsync.
    // ----------------------------------------------------------------

    [Fact]
    public async Task IndexExistsAsync_WhenIndexExists_ReturnsTrue()
    {
        var indexClientMock = CreateIndexClientMock();
        indexClientMock
            .Setup(c => c.GetIndexAsync("motorcycle-dirt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(new SearchIndex("motorcycle-dirt"), CreateMockResponse().Object));

        var factory = CreateFactory(indexClientMock);

        var exists = await factory.IndexExistsAsync(MotorcycleCategory.Dirt, CancellationToken.None);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task IndexExistsAsync_WhenIndexMissing_ReturnsFalse()
    {
        var indexClientMock = CreateIndexClientMock();
        indexClientMock
            .Setup(c => c.GetIndexAsync("motorcycle-touring", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Index was not found"));

        var factory = CreateFactory(indexClientMock);

        var exists = await factory.IndexExistsAsync(MotorcycleCategory.Touring, CancellationToken.None);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task IndexExistsAsync_WhenAuthorizationFails_PropagatesException()
    {
        var indexClientMock = CreateIndexClientMock();
        indexClientMock
            .Setup(c => c.GetIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "Forbidden"));

        var factory = CreateFactory(indexClientMock);

        var act = async () => await factory.IndexExistsAsync(MotorcycleCategory.Sport, CancellationToken.None);

        await act.Should().ThrowAsync<RequestFailedException>();
    }

    private static Mock<SearchIndexClient> CreateIndexClientMock()
        => new(new Uri("https://mcr-rag-dev-wcus-search.search.windows.net/"), new global::Azure.Identity.DefaultAzureCredential());

    private static Mock<Response> CreateMockResponse() => new();
}
