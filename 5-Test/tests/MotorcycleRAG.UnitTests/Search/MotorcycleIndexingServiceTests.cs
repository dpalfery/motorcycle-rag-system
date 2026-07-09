using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Search;

namespace MotorcycleRAG.UnitTests.Search;

/// <summary>
/// Unit tests for <see cref="MotorcycleIndexingService"/> index schema generation.
/// These are pure-logic tests: they exercise the internal index-definition factory and
/// assert the vector field configuration without making any Azure calls.
/// </summary>
public class MotorcycleIndexingServiceTests
{
    private const string TestIndexName = "test-motorcycle-index";
    private const int ExpectedVectorDimensions = 1536;

    private static readonly string[] LocatorFields =
        { "pageNumber", "chunkIndex", "pageRange" };

    /// <summary>
    /// Builds a <see cref="MotorcycleIndexingService"/> with harmless stand-in dependencies.
    /// <see cref="SearchIndexClient"/> is constructed with a dummy endpoint/credential; the
    /// index-definition factory never invokes it, so no network I/O occurs.
    /// </summary>
    private static MotorcycleIndexingService CreateService(string? indexName = null)
    {
        var searchOptions = Options.Create(new SearchOptions
        {
            IndexName = indexName ?? TestIndexName,
            BatchSize = 10
        });

        var dummyIndexClient = new SearchIndexClient(
            new Uri("https://localhost"),
            new AzureKeyCredential("dummy-credential"));

        return new MotorcycleIndexingService(
            searchClient: new Mock<IAzureSearchClient>().Object,
            indexClient: dummyIndexClient,
            searchOptions: searchOptions,
            logger: NullLogger<MotorcycleIndexingService>.Instance);
    }

    /// <summary>
    /// H2 regression guard: the <c>contentVector</c> field MUST declare 1536 dimensions to
    /// match the embedding pipeline output (Qwen3-Embedding-4B native 2560-dim, truncated to
    /// 1536 via Matryoshka slicing). A 3584-dim value (the 8B model) would silently break
    /// vector search at query time.
    /// </summary>
    [Fact]
    public void CreateIndexDefinition_DeclaresContentVectorWith1536Dimensions()
    {
        // Arrange
        var service = CreateService();

        // Act
        var index = service.CreateMotorcycleDocumentIndexDefinition();

        // Assert
        index.Should().NotBeNull();
        index.Name.Should().Be(TestIndexName);

        var vectorField = index.Fields
            .OfType<SearchField>()
            .FirstOrDefault(f => f.Name == "contentVector");

        vectorField.Should().NotBeNull("the index must include a contentVector field");
        vectorField!.IsSearchable.Should().BeTrue("vector fields must be searchable");
        vectorField.VectorSearchDimensions
            .Should().Be(ExpectedVectorDimensions,
                "Qwen3-Embedding-4B vectors are Matryoshka-truncated to 1536 dimensions");
        vectorField.VectorSearchDimensions
            .Should().NotBe(3584,
                "3584 dimensions correspond to the wrong (8B) model and would break vector search");
        vectorField.VectorSearchProfileName
            .Should().Be("vector-config", "the vector profile must reference a configured algorithm");
    }

    /// <summary>
    /// Ensures a single HNSW algorithm configuration and matching profile are wired up so the
    /// vector field's profile name resolves at index-creation time.
    /// </summary>
    [Fact]
    public void CreateIndexDefinition_ConfiguresVectorSearchAlgorithmAndProfile()
    {
        // Arrange
        var service = CreateService();

        // Act
        var index = service.CreateMotorcycleDocumentIndexDefinition();

        // Assert
        index.VectorSearch.Should().NotBeNull();
        index.VectorSearch!.Algorithms.Should().NotBeEmpty();
        index.VectorSearch.Algorithms
            .Should().Contain(a => a.Name == "vector-algo",
                "the vector-config profile references vector-algo");
        index.VectorSearch.Profiles
            .Should().Contain(p => p.Name == "vector-config",
                "the contentVector field references vector-config");
    }

    /// <summary>
    /// Verifies the index carries the key field and locator metadata fields required for
    /// citation support, so dimension-only changes don't silently drop schema fields.
    /// </summary>
    [Fact]
    public void CreateIndexDefinition_IncludesKeyAndLocatorFields()
    {
        // Arrange
        var service = CreateService();

        // Act
        var index = service.CreateMotorcycleDocumentIndexDefinition();

        // Assert
        var fieldNames = index.Fields.Select(f => f.Name).ToList();
        fieldNames.Should().Contain("id", "the document key field is required");
        fieldNames.Should().Contain(LocatorFields,
            "locator metadata fields must be present for citation support");
    }
}
