using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Persistence.Search;

namespace MotorcycleRAG.UnitTests.Search;

/// <summary>
/// Unit tests for <see cref="MotorcycleIndexingService"/>: index schema generation,
/// document indexing orchestration, and edge-case handling.
/// </summary>
public class MotorcycleIndexingServiceTests
{
    private const string TestIndexName = "test-motorcycle-index";
    private const int ExpectedVectorDimensions = 1536;
    private const int DefaultBatchSize = 10;

    private static readonly string[] LocatorFields =
        { "pageNumber", "chunkIndex", "pageRange" };

    // ─────────────────────────────────────────────────────────────────
    // Factory helpers
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="MotorcycleIndexingService"/> with a real
    /// <see cref="SearchIndexClient"/> for index-definition tests.
    /// </summary>
    private static MotorcycleIndexingService CreateService(
        string? indexName = null,
        int batchSize = DefaultBatchSize)
    {
        var searchOptions = Options.Create(new SearchOptions
        {
            IndexName = indexName ?? TestIndexName,
            BatchSize = batchSize
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
    /// Builds a <see cref="MotorcycleIndexingService"/> with a mock
    /// <see cref="IAzureSearchClient"/> for testing document indexing orchestration.
    /// </summary>
    private static (MotorcycleIndexingService Service, Mock<IAzureSearchClient> MockSearchClient)
        CreateServiceWithMockSearchClient(
            string? indexName = null,
            int batchSize = DefaultBatchSize)
    {
        var searchOptions = Options.Create(new SearchOptions
        {
            IndexName = indexName ?? TestIndexName,
            BatchSize = batchSize
        });

        var mockSearchClient = new Mock<IAzureSearchClient>();
        var dummyIndexClient = new SearchIndexClient(
            new Uri("https://localhost"),
            new AzureKeyCredential("dummy-credential"));

        var service = new MotorcycleIndexingService(
            searchClient: mockSearchClient.Object,
            indexClient: dummyIndexClient,
            searchOptions: searchOptions,
            logger: NullLogger<MotorcycleIndexingService>.Instance);

        return (service, mockSearchClient);
    }

    /// <summary>
    /// Creates a <see cref="MotorcycleDocument"/> with minimal required fields.
    /// </summary>
    private static MotorcycleDocument CreateDocument(string id, string title, string content)
    {
        return new MotorcycleDocument
        {
            Id = id,
            Title = title,
            Content = content,
            Type = Domain.Enums.DocumentType.Manual
        };
    }

    // ─────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithNullSearchClient_ShouldThrowArgumentNullException()
    {
        var opts = Options.Create(new SearchOptions { IndexName = "test" });
        var dummyIndexClient = new SearchIndexClient(
            new Uri("https://localhost"), new AzureKeyCredential("dummy"));

        var act = () => new MotorcycleIndexingService(
            searchClient: null!,
            indexClient: dummyIndexClient,
            searchOptions: opts,
            logger: NullLogger<MotorcycleIndexingService>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("searchClient");
    }

    [Fact]
    public void Constructor_WithNullIndexClient_ShouldThrowArgumentNullException()
    {
        var opts = Options.Create(new SearchOptions { IndexName = "test" });

        var act = () => new MotorcycleIndexingService(
            searchClient: new Mock<IAzureSearchClient>().Object,
            indexClient: null!,
            searchOptions: opts,
            logger: NullLogger<MotorcycleIndexingService>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("indexClient");
    }

    [Fact]
    public void Constructor_WithNullSearchOptions_ShouldThrowArgumentNullException()
    {
        var dummyIndexClient = new SearchIndexClient(
            new Uri("https://localhost"), new AzureKeyCredential("dummy"));

        var act = () => new MotorcycleIndexingService(
            searchClient: new Mock<IAzureSearchClient>().Object,
            indexClient: dummyIndexClient,
            searchOptions: null!,
            logger: NullLogger<MotorcycleIndexingService>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("searchOptions");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        var opts = Options.Create(new SearchOptions { IndexName = "test" });
        var dummyIndexClient = new SearchIndexClient(
            new Uri("https://localhost"), new AzureKeyCredential("dummy"));

        var act = () => new MotorcycleIndexingService(
            searchClient: new Mock<IAzureSearchClient>().Object,
            indexClient: dummyIndexClient,
            searchOptions: opts,
            logger: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ─────────────────────────────────────────────────────────────────
    // IndexDocumentsAsync — happy path (single batch)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IndexDocumentsAsync_WithDocumentsUnderBatchSize_ShouldIndexInSingleBatch()
    {
        // Arrange
        var (service, mockClient) = CreateServiceWithMockSearchClient(batchSize: 100);
        mockClient.Setup(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .Returns(Task.CompletedTask);

        var documents = new List<MotorcycleDocument>
        {
            CreateDocument("1", "Doc One", "Content one"),
            CreateDocument("2", "Doc Two", "Content two"),
            CreateDocument("3", "Doc Three", "Content three")
        };

        // Act
        var result = await service.IndexDocumentsAsync(documents);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.DocumentsProcessed.Should().Be(3);
        result.DocumentsIndexed.Should().Be(3);
        result.IndexName.Should().Be(TestIndexName);
        result.Errors.Should().BeEmpty();
        mockClient.Verify(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()),
            Times.Once, "should index exactly one batch");
    }

    [Fact]
    public async Task IndexDocumentsAsync_WithEmptyEnumerable_ShouldNotCallSearchClient()
    {
        // Arrange
        var (service, mockClient) = CreateServiceWithMockSearchClient();

        // Act
        var result = await service.IndexDocumentsAsync(Array.Empty<MotorcycleDocument>());

        // Assert
        result.Success.Should().BeTrue();
        result.DocumentsProcessed.Should().Be(0);
        result.DocumentsIndexed.Should().Be(0);
        mockClient.Verify(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()),
            Times.Never, "should not attempt to index empty input");
    }

    [Fact]
    public async Task IndexDocumentsAsync_WithSingleDocument_ShouldIndexOne()
    {
        // Arrange
        var (service, mockClient) = CreateServiceWithMockSearchClient(batchSize: 100);
        mockClient.Setup(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .Returns(Task.CompletedTask);

        var documents = new List<MotorcycleDocument>
        {
            CreateDocument("solo", "Solo Doc", "Solo content")
        };

        // Act
        var result = await service.IndexDocumentsAsync(documents);

        // Assert
        result.Success.Should().BeTrue();
        result.DocumentsProcessed.Should().Be(1);
        result.DocumentsIndexed.Should().Be(1);
        mockClient.Verify(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────
    // IndexDocumentsAsync — multiple batches
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IndexDocumentsAsync_WithDocumentsExceedingBatchSize_ShouldIndexInMultipleBatches()
    {
        // Arrange
        const int batchSize = 5;
        var (service, mockClient) = CreateServiceWithMockSearchClient(batchSize: batchSize);
        mockClient.Setup(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .Returns(Task.CompletedTask);

        var documents = Enumerable.Range(1, 12).Select(i =>
            CreateDocument(i.ToString(), $"Doc {i}", $"Content {i}")).ToList();

        // Act
        var result = await service.IndexDocumentsAsync(documents);

        // Assert
        result.Success.Should().BeTrue();
        result.DocumentsProcessed.Should().Be(12);
        result.DocumentsIndexed.Should().Be(12);
        mockClient.Verify(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()),
            Times.Exactly(3), "12 docs / batchSize 5 = 3 batches (5+5+2)");
    }

    [Fact]
    public async Task IndexDocumentsAsync_WithExactBatchSize_ShouldIndexInSingleBatch()
    {
        // Arrange
        const int batchSize = 5;
        var (service, mockClient) = CreateServiceWithMockSearchClient(batchSize: batchSize);
        mockClient.Setup(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .Returns(Task.CompletedTask);

        var documents = Enumerable.Range(1, batchSize).Select(i =>
            CreateDocument(i.ToString(), $"Doc {i}", $"Content {i}")).ToList();

        // Act
        var result = await service.IndexDocumentsAsync(documents);

        // Assert
        result.Success.Should().BeTrue();
        result.DocumentsProcessed.Should().Be(batchSize);
        result.DocumentsIndexed.Should().Be(batchSize);
        mockClient.Verify(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────
    // IndexDocumentsAsync — partial batch failure
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IndexDocumentsAsync_WhenBatchFails_ShouldContinueWithRemainingBatches()
    {
        // Arrange
        const int batchSize = 3;
        var (service, mockClient) = CreateServiceWithMockSearchClient(batchSize: batchSize);

        // First batch succeeds, second batch fails, third batch succeeds
        var callCount = 0;
        mockClient.Setup(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 2)
                    throw new InvalidOperationException("Batch 2 indexing failed");
                return Task.CompletedTask;
            });

        // 9 documents -> 3 batches of 3
        var documents = Enumerable.Range(1, 9).Select(i =>
            CreateDocument(i.ToString(), $"Doc {i}", $"Content {i}")).ToList();

        // Act
        var result = await service.IndexDocumentsAsync(documents);

        // Assert
        result.Success.Should().BeFalse();
        result.DocumentsProcessed.Should().Be(9);
        result.DocumentsIndexed.Should().Be(6); // 3 from batch 1 + 3 from batch 3
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Should().Contain("Batch 2 indexing failed");
        mockClient.Verify(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task IndexDocumentsAsync_WhenAllBatchesFail_ShouldReportAllErrors()
    {
        // Arrange
        const int batchSize = 2;
        var (service, mockClient) = CreateServiceWithMockSearchClient(batchSize: batchSize);
        mockClient.Setup(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .ThrowsAsync(new InvalidOperationException("Batch failed"));

        var documents = Enumerable.Range(1, 4).Select(i =>
            CreateDocument(i.ToString(), $"Doc {i}", $"Content {i}")).ToList();

        // Act
        var result = await service.IndexDocumentsAsync(documents);

        // Assert
        result.Success.Should().BeFalse();
        result.DocumentsProcessed.Should().Be(4);
        result.DocumentsIndexed.Should().Be(0);
        result.Errors.Should().HaveCount(2);
        mockClient.Verify(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()),
            Times.Exactly(2));
    }

    // ─────────────────────────────────────────────────────────────────
    // IndexDocumentsAsync — complete failure (outer catch)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IndexDocumentsAsync_WhenOuterExceptionThrown_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var (service, mockClient) = CreateServiceWithMockSearchClient(batchSize: 10);
        // First call succeeds (inner), but we cause an exception in the batch enumeration
        // by making the documents collection throw after enumeration starts.
        mockClient.Setup(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .Returns(Task.CompletedTask);

        // Null input hits outer catch (ArgumentNullException from .ToList wrapped as InvalidOperationException)
        // We test this via null documents

        // Act
        var act = () => service.IndexDocumentsAsync(null!);

        // Assert — the outer catch wraps ArgumentNullException from .ToList()
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*documents*{TestIndexName}*");
    }

    // ─────────────────────────────────────────────────────────────────
    // IndexDocumentsAsync — null input (covered by
    // IndexDocumentsAsync_WhenOuterExceptionThrown above)
    // ─────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────
    // CreateMotorcycleDocumentIndexDefinition
    // ─────────────────────────────────────────────────────────────────

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

    /// <summary>
    /// Verifies required core document fields are present in the index.
    /// </summary>
    [Fact]
    public void CreateIndexDefinition_IncludesCoreDocumentFields()
    {
        // Arrange
        var service = CreateService();

        // Act
        var index = service.CreateMotorcycleDocumentIndexDefinition();

        // Assert
        var fieldNames = index.Fields.Select(f => f.Name).ToList();
        fieldNames.Should().Contain("title");
        fieldNames.Should().Contain("content");
        fieldNames.Should().Contain("documentType");
        fieldNames.Should().Contain("make");
        fieldNames.Should().Contain("model");
        fieldNames.Should().Contain("year");
    }

    /// <summary>
    /// Verifies source metadata fields are present for citation and provenance tracking.
    /// </summary>
    [Fact]
    public void CreateIndexDefinition_IncludesSourceMetadataFields()
    {
        // Arrange
        var service = CreateService();

        // Act
        var index = service.CreateMotorcycleDocumentIndexDefinition();

        // Assert
        var fieldNames = index.Fields.Select(f => f.Name).ToList();
        fieldNames.Should().Contain("sourceFile");
        fieldNames.Should().Contain("sourceUrl");
        fieldNames.Should().Contain("author");
        fieldNames.Should().Contain("publishedDate");
        fieldNames.Should().Contain("tags");
    }

    /// <summary>
    /// The id field must be a key field with filterability.
    /// </summary>
    [Fact]
    public void CreateIndexDefinition_IdFieldIsKeyAndFilterable()
    {
        // Arrange
        var service = CreateService();

        // Act
        var index = service.CreateMotorcycleDocumentIndexDefinition();

        // Assert
        var idField = index.Fields.FirstOrDefault(f => f.Name == "id");
        idField.Should().NotBeNull();
        idField!.IsKey.Should().BeTrue();
        idField.IsFilterable.Should().BeTrue();
    }

    /// <summary>
    /// The index uses the configured index name from SearchOptions.
    /// </summary>
    [Fact]
    public void CreateIndexDefinition_UsesConfiguredIndexName()
    {
        // Arrange
        const string customName = "custom-index-name";
        var service = CreateService(indexName: customName);

        // Act
        var index = service.CreateMotorcycleDocumentIndexDefinition();

        // Assert
        index.Name.Should().Be(customName);
    }

    /// <summary>
    /// The HNSW algorithm parameters (M, EfConstruction, EfSearch) must match the
    /// expected configuration for optimal recall/performance tradeoff.
    /// </summary>
    [Fact]
    public void CreateIndexDefinition_HnswAlgorithmHasExpectedParameters()
    {
        // Arrange
        var service = CreateService();

        // Act
        var index = service.CreateMotorcycleDocumentIndexDefinition();

        // Assert
        var algo = index.VectorSearch!.Algorithms
            .OfType<HnswAlgorithmConfiguration>()
            .First(a => a.Name == "vector-algo");
        algo.Should().NotBeNull();
        algo.Parameters.Should().NotBeNull();
        algo.Parameters!.M.Should().Be(4);
        algo.Parameters.EfConstruction.Should().Be(400);
        algo.Parameters.EfSearch.Should().Be(500);
    }

    // ─────────────────────────────────────────────────────────────────
    // GetIndexingStatisticsAsync — error path
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the SearchIndexClient cannot reach the endpoint (dummy localhost),
    /// GetIndexingStatisticsAsync should catch the exception and return an
    /// error-populated statistics object instead of throwing.
    /// </summary>
    [Fact]
    public async Task GetIndexingStatisticsAsync_WhenClientUnreachable_ShouldReturnErrorStatistics()
    {
        // Arrange: dummy SearchIndexClient at localhost will fail to connect
        var service = CreateService();

        // Act
        var result = await service.GetIndexingStatisticsAsync();

        // Assert: should not throw; error message populated
        result.Should().NotBeNull();
        result.ErrorMessage.Should().NotBeNullOrEmpty(
            "should capture the connection failure message");
        result.Indexes.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────
    // RebuildIndexAsync — error path
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the SearchIndexClient cannot reach the endpoint (dummy localhost),
    /// RebuildIndexAsync should catch the exception and return a failure result
    /// instead of throwing.
    /// </summary>
    [Fact]
    public async Task RebuildIndexAsync_WhenClientUnreachable_ShouldReturnFailureResult()
    {
        // Arrange: dummy SearchIndexClient at localhost will fail to connect
        var service = CreateService();

        // Act
        var result = await service.RebuildIndexAsync();

        // Assert: should not throw; failure result returned
        result.Should().NotBeNull();
        result.Success.Should().BeFalse(
            "should return failure when SearchIndexClient is unreachable");
        result.Message.Should().Contain("Index rebuild failed");
        result.Errors.Should().NotBeEmpty();
    }
}
