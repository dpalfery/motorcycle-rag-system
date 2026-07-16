using Azure;
using Azure.Core;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.ValueObjects;
using MotorcycleRAG.Persistence.Azure;
using MotorcycleRAG.Persistence.Azure.Search;

namespace MotorcycleRAG.Persistence.Tests.Azure.Search;

/// <summary>
/// Unit tests for the concrete <see cref="SearchClientFactory"/>.
///
/// <b>Coverage exemption:</b> <see cref="SearchClientFactory.GetClient(MotorcycleCategory)"/>
/// and <see cref="SearchClientFactory.GetDefaultClient()"/> internally construct a real
/// <c>new SearchClient(endpoint, indexName, credential)</c> that requires a live Azure
/// Search endpoint. The category-normalization and caching logic inside those methods is
/// therefore exercised only at the integration-test layer or with a real dev endpoint.
/// Extracting <c>SearchClient</c> creation behind a <c>Func</c> factory would fully
/// unit-test the remaining branches without this limitation; this is tracked as a future
/// refactoring note.
/// </summary>
public sealed class SearchClientFactoryTests
{
    private const string ValidEndpoint = "https://search-test.search.windows.net";

    private static IAzureCredentialProvider CreateCredentialProvider()
    {
        var provider = new Mock<IAzureCredentialProvider>();
        provider.Setup(x => x.GetSearchCredential()).Returns(new global::Azure.Identity.DefaultAzureCredential());
        return provider.Object;
    }

    private static IOptions<AzureFoundryOptions> CreateValidOptions(string? endpoint = ValidEndpoint) =>
        TestHelpers.OptionsFor(new AzureFoundryOptions
        {
            SearchServiceEndpoint = endpoint!
        });

    private static SearchClientFactory CreateSut(
        string? endpoint = ValidEndpoint,
        SearchIndexClient? indexClient = null,
        IAzureCredentialProvider? credentialProvider = null)
    {
        return new SearchClientFactory(
            CreateValidOptions(endpoint),
            indexClient ?? new Mock<SearchIndexClient>().Object,
            credentialProvider ?? CreateCredentialProvider());
    }

    // -----------------------------------------------------------------------
    // Constructor – argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenAzureOptionsIsNull()
    {
        var act = () => new SearchClientFactory(
            null!,
            new Mock<SearchIndexClient>().Object,
            CreateCredentialProvider());

        act.Should().Throw<ArgumentNullException>().WithParameterName("azureOptions");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenCredentialProviderIsNull()
    {
        var act = () => new SearchClientFactory(
            CreateValidOptions(),
            new Mock<SearchIndexClient>().Object,
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("credentialProvider");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenIndexClientIsNull()
    {
        var act = () => new SearchClientFactory(
            CreateValidOptions(),
            null!,
            CreateCredentialProvider());

        act.Should().Throw<ArgumentNullException>().WithParameterName("indexClient");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentException_WhenOptionsValueIsNull()
    {
        var optionsMock = new Mock<IOptions<AzureFoundryOptions>>();
        optionsMock.Setup(x => x.Value).Returns((AzureFoundryOptions)null!);

        var act = () => new SearchClientFactory(
            optionsMock.Object,
            new Mock<SearchIndexClient>().Object,
            CreateCredentialProvider());

        act.Should().Throw<ArgumentException>()
            .WithParameterName("azureOptions")
            .WithMessage("*AzureFoundryOptions value is null*");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentException_WhenEndpointIsNotValidUri()
    {
        var act = () => new SearchClientFactory(
            CreateValidOptions("not a valid uri !!!"),
            new Mock<SearchIndexClient>().Object,
            CreateCredentialProvider());

        act.Should().Throw<ArgumentException>()
            .WithParameterName("azureOptions")
            .WithMessage("*is not a valid absolute URI*");
    }

    [Fact]
    public void Constructor_WhenOptionsAndIndexClientAreValid_ShouldNotThrow()
    {
        var act = () => CreateSut();

        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // DefaultCategory
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultCategory_ShouldReturnSport()
    {
        var sut = CreateSut();

        sut.DefaultCategory.Should().Be(MotorcycleCategory.Sport);
    }

    // -----------------------------------------------------------------------
    // AllCategories
    // -----------------------------------------------------------------------

    [Fact]
    public void AllCategories_ShouldReturnAllFourCategories()
    {
        var sut = CreateSut();

        var allCategories = sut.AllCategories;

        allCategories.Should().HaveCount(4);
        allCategories.Should().Contain(MotorcycleCategory.Dirt);
        allCategories.Should().Contain(MotorcycleCategory.Touring);
        allCategories.Should().Contain(MotorcycleCategory.Sport);
        allCategories.Should().Contain(MotorcycleCategory.Cruiser);
    }

    // -----------------------------------------------------------------------
    // GetIndexName
    // -----------------------------------------------------------------------

    [Fact]
    public void GetIndexName_ForDirt_ShouldReturnMotorcycleDirt()
    {
        var sut = CreateSut();

        var indexName = sut.GetIndexName(MotorcycleCategory.Dirt);

        indexName.Should().Be("motorcycle-dirt");
    }

    [Fact]
    public void GetIndexName_ForSport_ShouldReturnMotorcycleSport()
    {
        var sut = CreateSut();

        var indexName = sut.GetIndexName(MotorcycleCategory.Sport);

        indexName.Should().Be("motorcycle-sport");
    }

    [Fact]
    public void GetIndexName_ForTouring_ShouldReturnMotorcycleTouring()
    {
        var sut = CreateSut();

        var indexName = sut.GetIndexName(MotorcycleCategory.Touring);

        indexName.Should().Be("motorcycle-touring");
    }

    [Fact]
    public void GetIndexName_ForCruiser_ShouldReturnMotorcycleCruiser()
    {
        var sut = CreateSut();

        var indexName = sut.GetIndexName(MotorcycleCategory.Cruiser);

        indexName.Should().Be("motorcycle-cruiser");
    }

    [Fact]
    public void GetIndexName_ForUndefinedCategory_ShouldFallBackToDefaultIndex()
    {
        var sut = CreateSut();

        var indexName = sut.GetIndexName(default(MotorcycleCategory));

        // Fallback to default category (Sport → "motorcycle-sport")
        indexName.Should().Be("motorcycle-sport");
    }

    // -----------------------------------------------------------------------
    // GetDefaultClient — delegates to GetClient(DefaultCategory)
    // -----------------------------------------------------------------------

    /// <summary>
    /// GetDefaultClient calls GetClient(DefaultCategory) which internally
    /// does <c>new SearchClient(...)</c>. Verifying the exact return is not
    /// possible in a unit test (no live endpoint), but the method does not
    /// throw construction errors when valid args are provided. The integration
    /// layer covers the full round-trip.
    /// </summary>
    [Fact]
    public void GetDefaultClient_ShouldNotThrow_WhenConstructorArgsAreValid()
    {
        var sut = CreateSut();

        var act = () => sut.GetDefaultClient();

        // GetDefaultClient internally creates a SearchClient; the factory's
        // constructor-level validation should already have passed, so the
        // only remaining concern is whether the SearchClient constructor
        // throws.  In a unit-test context with a bad endpoint the SDK may
        // still construct the object lazily — we verify this does not throw
        // synchronously.
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // IndexExistsAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IndexExistsAsync_WhenIndexExists_ShouldReturnTrue()
    {
        var indexClientMock = new Mock<SearchIndexClient>();
        var searchIndex = new SearchIndex("motorcycle-sport");
        var mockRawResponse = new Mock<global::Azure.Response>();
        var azureResponse = global::Azure.Response.FromValue(searchIndex, mockRawResponse.Object);
        indexClientMock
            .Setup(x => x.GetIndexAsync("motorcycle-sport", It.IsAny<CancellationToken>()))
            .ReturnsAsync(azureResponse);

        var sut = new SearchClientFactory(
            CreateValidOptions(),
            indexClientMock.Object,
            CreateCredentialProvider());

        var exists = await sut.IndexExistsAsync(MotorcycleCategory.Sport);

        exists.Should().BeTrue();
        indexClientMock.Verify(x => x.GetIndexAsync("motorcycle-sport", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IndexExistsAsync_WhenIndexDoesNotExist_ShouldReturnFalse()
    {
        var indexClientMock = new Mock<SearchIndexClient>();
        indexClientMock
            .Setup(x => x.GetIndexAsync("motorcycle-dirt", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Index not found"));

        var sut = new SearchClientFactory(
            CreateValidOptions(),
            indexClientMock.Object,
            CreateCredentialProvider());

        var exists = await sut.IndexExistsAsync(MotorcycleCategory.Dirt);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task IndexExistsAsync_WhenNon404RequestFailedException_ShouldPropagate()
    {
        var indexClientMock = new Mock<SearchIndexClient>();
        indexClientMock
            .Setup(x => x.GetIndexAsync("motorcycle-sport", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, "Internal server error"));

        var sut = new SearchClientFactory(
            CreateValidOptions(),
            indexClientMock.Object,
            CreateCredentialProvider());

        var act = async () => await sut.IndexExistsAsync(MotorcycleCategory.Sport);

        await act.Should().ThrowAsync<RequestFailedException>()
            .Where(ex => ex.Status == 500);
    }

    [Fact]
    public async Task IndexExistsAsync_WithCancellationToken_ShouldPassTokenToGetIndex()
    {
        var indexClientMock = new Mock<SearchIndexClient>();
        var searchIndex = new SearchIndex("motorcycle-touring");
        var mockRawResponse = new Mock<global::Azure.Response>();
        var azureResponse = global::Azure.Response.FromValue(searchIndex, mockRawResponse.Object);
        var cts = new CancellationTokenSource();

        indexClientMock
            .Setup(x => x.GetIndexAsync("motorcycle-touring", cts.Token))
            .ReturnsAsync(azureResponse);

        var sut = new SearchClientFactory(
            CreateValidOptions(),
            indexClientMock.Object,
            CreateCredentialProvider());

        var exists = await sut.IndexExistsAsync(MotorcycleCategory.Touring, cts.Token);

        exists.Should().BeTrue();
        indexClientMock.Verify(x => x.GetIndexAsync("motorcycle-touring", cts.Token), Times.Once);
    }

    [Fact]
    public async Task IndexExistsAsync_ForUndefinedCategory_ShouldFallBackToDefaultIndex()
    {
        var indexClientMock = new Mock<SearchIndexClient>();
        var searchIndex = new SearchIndex("motorcycle-sport");
        var mockRawResponse = new Mock<global::Azure.Response>();
        var azureResponse = global::Azure.Response.FromValue(searchIndex, mockRawResponse.Object);

        // Undefined category resolves to default (Sport → "motorcycle-sport")
        indexClientMock
            .Setup(x => x.GetIndexAsync("motorcycle-sport", It.IsAny<CancellationToken>()))
            .ReturnsAsync(azureResponse);

        var sut = new SearchClientFactory(
            CreateValidOptions(),
            indexClientMock.Object,
            CreateCredentialProvider());

        var exists = await sut.IndexExistsAsync(default(MotorcycleCategory));

        exists.Should().BeTrue();
    }
}
