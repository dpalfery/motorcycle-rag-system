using System.Reflection;
using System.Text;
using global::Azure;
using Azure.Core;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using SearchOptions = MotorcycleRAG.Core.Options.SearchOptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Exceptions;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.ValueObjects;
using MotorcycleRAG.Persistence.Azure.Search;
using System.ClientModel.Primitives;

namespace MotorcycleRAG.Persistence.Tests.Azure.Search;

public sealed class ChunkIndexingServiceTests
{
    private readonly Mock<ISearchClientFactory> _clientFactoryMock = new();
    private readonly Mock<IMotorcycleCategoryClassifier> _categoryClassifierMock = new();
    private readonly Mock<ISearchIndexResiliencePipeline> _resiliencePipelineMock = new();
    private readonly SearchOptions _searchOptions = new()
    {
        MaxSearchResults = 50,
        BatchSize = 100,
        BatchIndexTimeoutSeconds = 30,
        IndexName = "motorcycle-sport"
    };

    private ChunkIndexingService CreateSut(SearchOptions? options = null) =>
        new(
            _clientFactoryMock.Object,
            _categoryClassifierMock.Object,
            _resiliencePipelineMock.Object,
            TestHelpers.OptionsFor(options ?? _searchOptions),
            TestHelpers.CreateNullLogger<ChunkIndexingService>());

    // ---- Constructor ----

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenClientFactoryIsNull()
    {
        var act = () => new ChunkIndexingService(
            null!,
            _categoryClassifierMock.Object,
            _resiliencePipelineMock.Object,
            TestHelpers.OptionsFor(_searchOptions),
            TestHelpers.CreateNullLogger<ChunkIndexingService>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenCategoryClassifierIsNull()
    {
        var act = () => new ChunkIndexingService(
            _clientFactoryMock.Object,
            null!,
            _resiliencePipelineMock.Object,
            TestHelpers.OptionsFor(_searchOptions),
            TestHelpers.CreateNullLogger<ChunkIndexingService>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("categoryClassifier");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenResiliencePipelineIsNull()
    {
        var act = () => new ChunkIndexingService(
            _clientFactoryMock.Object,
            _categoryClassifierMock.Object,
            null!,
            TestHelpers.OptionsFor(_searchOptions),
            TestHelpers.CreateNullLogger<ChunkIndexingService>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("resiliencePipeline");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenSearchOptionsIsNull()
    {
        var act = () => new ChunkIndexingService(
            _clientFactoryMock.Object,
            _categoryClassifierMock.Object,
            _resiliencePipelineMock.Object,
            null!,
            TestHelpers.CreateNullLogger<ChunkIndexingService>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("searchOptions");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var act = () => new ChunkIndexingService(
            _clientFactoryMock.Object,
            _categoryClassifierMock.Object,
            _resiliencePipelineMock.Object,
            TestHelpers.OptionsFor(_searchOptions),
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ---- ResolveChunkCategoryAsync (internal) ----

    [Fact]
    public async Task ResolveChunkCategoryAsync_WithValidEmbeddedCategory_ShouldReturnThatCategory()
    {
        var sut = CreateSut();
        var chunk = new ChunkIndexingService.ChunkIndexRecord
        {
            Id = "chunk-1",
            Category = "dirt",
            Content = "Dirt bike engine maintenance"
        };
        var cache = new Dictionary<string, MotorcycleCategory>();

        var result = await sut.ResolveChunkCategoryAsync(chunk, cache, CancellationToken.None);

        result.Should().Be(MotorcycleCategory.Dirt);
        _categoryClassifierMock.Verify(
            x => x.ResolveCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveChunkCategoryAsync_WithoutCategoryButWithMakeModel_ShouldUseClassifier()
    {
        var sut = CreateSut();
        var chunk = new ChunkIndexingService.ChunkIndexRecord
        {
            Id = "chunk-2",
            Category = "", // empty, not a valid category
            Make = "Honda",
            Model = "CRF450",
            Content = "Engine specs"
        };
        var cache = new Dictionary<string, MotorcycleCategory>();
        _categoryClassifierMock
            .Setup(x => x.ResolveCategoryAsync("Honda", "CRF450", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MotorcycleCategory.Dirt);

        var result = await sut.ResolveChunkCategoryAsync(chunk, cache, CancellationToken.None);

        result.Should().Be(MotorcycleCategory.Dirt);
        _categoryClassifierMock.Verify(
            x => x.ResolveCategoryAsync("Honda", "CRF450", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveChunkCategoryAsync_WithCachedMakeModel_ShouldNotCallClassifierAgain()
    {
        var sut = CreateSut();
        var chunk1 = new ChunkIndexingService.ChunkIndexRecord
        {
            Id = "chunk-a",
            Make = "Honda",
            Model = "CRF450"
        };
        var chunk2 = new ChunkIndexingService.ChunkIndexRecord
        {
            Id = "chunk-b",
            Make = "Honda",
            Model = "CRF450" // same make/model
        };
        var cache = new Dictionary<string, MotorcycleCategory>();
        _categoryClassifierMock
            .Setup(x => x.ResolveCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MotorcycleCategory.Dirt);

        await sut.ResolveChunkCategoryAsync(chunk1, cache, CancellationToken.None);
        var result = await sut.ResolveChunkCategoryAsync(chunk2, cache, CancellationToken.None);

        result.Should().Be(MotorcycleCategory.Dirt);
        _categoryClassifierMock.Verify(
            x => x.ResolveCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveChunkCategoryAsync_WithNoCategoryAndNoMakeModel_ShouldFallBackToDefault()
    {
        var sut = CreateSut();
        var chunk = new ChunkIndexingService.ChunkIndexRecord
        {
            Id = "chunk-no-info",
            Category = "",
            Make = null,
            Model = null,
            Content = "Generic content"
        };
        var cache = new Dictionary<string, MotorcycleCategory>();
        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>())).Returns("motorcycle-sport");

        var result = await sut.ResolveChunkCategoryAsync(chunk, cache, CancellationToken.None);

        result.Should().Be(MotorcycleCategory.Sport);
        _categoryClassifierMock.Verify(
            x => x.ResolveCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveChunkCategoryAsync_WhenClassifierReturnsUndefined_ShouldFallBackToDefault()
    {
        var sut = CreateSut();
        var chunk = new ChunkIndexingService.ChunkIndexRecord
        {
            Id = "chunk-unknown",
            Make = "UnknownBrand",
            Model = "UnknownModel"
        };
        var cache = new Dictionary<string, MotorcycleCategory>();
        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>())).Returns("motorcycle-sport");
        _categoryClassifierMock
            .Setup(x => x.ResolveCategoryAsync("UnknownBrand", "UnknownModel", It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(MotorcycleCategory)); // undefined

        var result = await sut.ResolveChunkCategoryAsync(chunk, cache, CancellationToken.None);

        result.Should().Be(MotorcycleCategory.Sport);
        cache["UnknownBrand|UnknownModel"].Should().Be(MotorcycleCategory.Sport);
    }

    // ---- GroupByCategoryAsync (internal) ----

    [Fact]
    public async Task GroupByCategoryAsync_ShouldGroupChunksByResolvedCategory()
    {
        var sut = CreateSut();
        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>())).Returns((MotorcycleCategory c) => $"motorcycle-{c.Value}");

        var chunks = new List<ChunkIndexingService.ChunkIndexRecord>
        {
            new() { Id = "d1", Category = "dirt", Content = "Dirt content 1" },
            new() { Id = "s1", Category = "sport", Content = "Sport content 1" },
            new() { Id = "d2", Category = "dirt", Content = "Dirt content 2" },
        };

        var groups = await sut.GroupByCategoryAsync(chunks, CancellationToken.None);

        groups.Should().ContainKey(MotorcycleCategory.Dirt);
        groups[MotorcycleCategory.Dirt].Should().HaveCount(2);
        groups.Should().ContainKey(MotorcycleCategory.Sport);
        groups[MotorcycleCategory.Sport].Should().HaveCount(1);
    }

    // ---- IndexFromJsonlAsync - validation ----

    [Fact]
    public async Task IndexFromJsonlAsync_ShouldThrowArgumentNullException_WhenStreamIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.IndexFromJsonlAsync(null!, "upload-1", Guid.Empty, Guid.Empty, null);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("jsonlStream");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task IndexFromJsonlAsync_ShouldThrowArgumentException_WhenUploadIdIsBlank(string? uploadId)
    {
        var sut = CreateSut();
        using var stream = CreateJsonlStream();

        var act = async () => await sut.IndexFromJsonlAsync(stream, uploadId!, Guid.Empty, Guid.Empty, null);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("uploadId");
    }

    // ---- IndexFromJsonlAsync - empty stream ----

    [Fact]
    public async Task IndexFromJsonlAsync_WithEmptyStream_ShouldReturnZeroOutcomes()
    {
        var sut = CreateSut();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(""));

        var result = await sut.IndexFromJsonlAsync(stream, "upload-empty", Guid.Empty, Guid.Empty, null);

        result.TotalParsed.Should().Be(0);
        result.BatchCount.Should().Be(0);
        result.Outcomes.Should().BeEmpty();
    }

    // ---- IndexFromJsonlAsync - skips malformed lines ----

    [Fact]
    public async Task IndexFromJsonlAsync_ShouldParseOnlyValidJsonLines_SkippingMalformedOnes()
    {
        var sut = CreateSut();
        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>())).Returns("motorcycle-sport");
        _clientFactoryMock.Setup(x => x.IndexExistsAsync(It.IsAny<MotorcycleCategory>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var jsonl = "NOT VALID JSON\n{\"id\":\"chunk-valid\",\"category\":\"sport\",\"content\":\"test content\",\"title\":\"test\",\"documentType\":\"manual\",\"contentVector\":[0.1,0.2]}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));

        var result = await sut.IndexFromJsonlAsync(stream, "upload-skip", Guid.Empty, Guid.Empty, null);

        // Only the valid JSON line is parsed; the malformed line is logged and skipped.
        result.TotalParsed.Should().Be(1);
        // Malformed lines contribute zero outcomes; only the valid parsed chunk advances.
        result.Outcomes.Should().NotBeEmpty();
    }

    // ---- ResolveChunkCategoryAsync error path ----

    [Fact]
    public async Task ResolveChunkCategoryAsync_WhenClassifierThrows_ShouldPropagateException()
    {
        var sut = CreateSut();
        var chunk = new ChunkIndexingService.ChunkIndexRecord
        {
            Id = "chunk-error",
            Category = "",
            Make = "Honda",
            Model = "CRF450",
            Content = "Engine specs"
        };
        var cache = new Dictionary<string, MotorcycleCategory>();
        _categoryClassifierMock
            .Setup(x => x.ResolveCategoryAsync("Honda", "CRF450", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Classifier down"));

        var act = async () => await sut.ResolveChunkCategoryAsync(chunk, cache, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Classifier down");
    }

    // ---- GroupByCategoryAsync empty list ----

    [Fact]
    public async Task GroupByCategoryAsync_WithEmptyList_ShouldReturnEmptyDictionary()
    {
        var sut = CreateSut();
        var chunks = new List<ChunkIndexingService.ChunkIndexRecord>();

        var groups = await sut.GroupByCategoryAsync(chunks, CancellationToken.None);

        groups.Should().NotBeNull();
        groups.Should().BeEmpty();
    }

    // ---- IndexFromJsonlAsync with whitespace-only lines ----

    [Fact]
    public async Task IndexFromJsonlAsync_ShouldSkipBlankLines()
    {
        var sut = CreateSut();
        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>())).Returns("motorcycle-sport");
        _clientFactoryMock.Setup(x => x.IndexExistsAsync(It.IsAny<MotorcycleCategory>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // JSONL with blank lines between valid records
        var jsonl = "\n\n{\"id\":\"chunk-1\",\"category\":\"sport\",\"content\":\"test\",\"title\":\"t\",\"documentType\":\"manual\",\"contentVector\":[0.1,0.2]}\n  \n{\"id\":\"chunk-2\",\"category\":\"sport\",\"content\":\"test2\",\"title\":\"t2\",\"documentType\":\"manual\",\"contentVector\":[0.3,0.4]}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));

        var result = await sut.IndexFromJsonlAsync(stream, "upload-blank-lines", Guid.Empty, Guid.Empty, null);

        // Only 2 valid records should be parsed, blank lines skipped
        result.TotalParsed.Should().Be(2);
    }

    // ---- IndexFromJsonlAsync - precheck: index existence ----

    [Fact]
    public async Task IndexFromJsonlAsync_WhenIndexDoesNotExist_ShouldThrowSearchIndexNotFoundException()
    {
        var sut = CreateSut();
        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>())).Returns((MotorcycleCategory c) => $"motorcycle-{c.Value}");
        // Return false for the resolved category so the precheck fails
        _clientFactoryMock
            .Setup(x => x.IndexExistsAsync(MotorcycleCategory.Dirt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var jsonl = "{\"id\":\"chunk-1\",\"category\":\"dirt\",\"content\":\"test\",\"title\":\"t\",\"documentType\":\"manual\",\"contentVector\":[0.1,0.2]}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));

        var act = async () => await sut.IndexFromJsonlAsync(stream, "upload-missing-index", Guid.Empty, Guid.Empty, null);

        await act.Should().ThrowAsync<SearchIndexNotFoundException>()
            .WithMessage("*motorcycle-dirt*");
    }

    [Fact]
    public async Task IndexFromJsonlAsync_WhenIndexExists_ShouldNotThrowSearchIndexNotFoundException()
    {
        // Use a real resilience pipeline (not the mocked one whose Pipeline returns null)
        // so the batch processing code runs through a real Polly pipeline rather than
        // silently swallowing a NullReferenceException. The SearchClient is mocked to
        // throw a non-missing-index exception, which the pipeline propagates and the
        // catch (Exception) block swallows (IsNonTransient returns false). The test
        // verifies the precheck-succeed path: when IndexExistsAsync returns true, no
        // SearchIndexNotFoundException is thrown.
        //
        // Note: several pre-existing IndexFromJsonlAsync tests in this file also rely on
        // the mocked pipeline (Mock<ISearchIndexResiliencePipeline> whose Pipeline
        // property is null). That NullReferenceException is silently swallowed at line
        // ~339 of ChunkIndexingService.cs (catch (Exception) when IsNonTransient is
        // false), making those tests pass for the wrong reason. Those tests are left
        // as-is per scope; this test demonstrates the corrected pattern.
        var realPipeline = new SearchIndexResiliencePipelineProvider();

        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns((MotorcycleCategory c) => $"motorcycle-{c.Value}");
        _clientFactoryMock
            .Setup(x => x.IndexExistsAsync(It.IsAny<MotorcycleCategory>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Mock the SearchClient returned by GetClient to throw a non-SearchIndexNotFoundException
        // so the precheck-succeed path is exercised end-to-end without needing a real Azure Search.
        var searchClientMock = new Mock<SearchClient>();
        searchClientMock
            .Setup(c => c.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<ChunkIndexingService.ChunkIndexRecord>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated batch failure"));
        _clientFactoryMock.Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>()))
            .Returns(searchClientMock.Object);

        var sut = new ChunkIndexingService(
            _clientFactoryMock.Object,
            _categoryClassifierMock.Object,
            realPipeline,
            TestHelpers.OptionsFor(_searchOptions),
            TestHelpers.CreateNullLogger<ChunkIndexingService>());

        var jsonl = "{\"id\":\"chunk-10\",\"category\":\"sport\",\"content\":\"test\",\"title\":\"t\",\"documentType\":\"manual\",\"contentVector\":[0.1,0.2]}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));

        // The precheck passed (index exists). The batch processing fails but NOT with
        // SearchIndexNotFoundException — the exception is swallowed by the catch (Exception)
        // block because IsNonTransient(InvalidOperationException) returns false.
        // IndexFromJsonlAsync completes without throwing.
        var act = async () => await sut.IndexFromJsonlAsync(stream, "upload-index-exists", Guid.Empty, Guid.Empty, null);
        await act.Should().NotThrowAsync();
    }

    // ── Note about pre-existing tests with swallowed NullReferenceException ──
    // Several IndexFromJsonlAsync tests in this file (ShouldParseOnlyValidJsonLines,
    // ShouldSkipBlankLines, and their kin) use the mocked resilience pipeline whose
    // Pipeline property is null. That NullReferenceException is silently swallowed
    // inside IndexBatchForCategoryAsync's catch (Exception) block because
    // IsNonTransient(NullReferenceException) returns false. These tests pass, but
    // for the wrong reason — they do not actually exercise the indexing code path.
    // Fixing them is deferred per scoping guidance; future test work should follow
    // the pattern shown in IndexFromJsonlAsync_WhenIndexExists_ShouldNotThrowSearchIndexNotFoundException
    // above (real SearchIndexResiliencePipelineProvider + Mock<AzureSearchClient>).

    // ---- ResolveChunkCategoryAsync - edge case: undefined category with null Category string ----

    [Fact]
    public async Task ResolveChunkCategoryAsync_WithUndefinedEmbeddedCategoryAndNoMakeModel_ShouldFallBackToDefault()
    {
        var sut = CreateSut();
        var chunk = new ChunkIndexingService.ChunkIndexRecord
        {
            Id = "chunk-undefined",
            Category = "invalid-category-value",
            Make = null,
            Model = null,
            Content = "Generic content"
        };
        var cache = new Dictionary<string, MotorcycleCategory>();
        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Cruiser);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>())).Returns("motorcycle-cruiser");

        var result = await sut.ResolveChunkCategoryAsync(chunk, cache, CancellationToken.None);

        result.Should().Be(MotorcycleCategory.Cruiser);
    }

    // ---- ResolveChunkCategoryAsync - make-only (no model) input ----

    [Fact]
    public async Task ResolveChunkCategoryAsync_WithMakeButNoModel_ShouldUseClassifier()
    {
        var sut = CreateSut();
        var chunk = new ChunkIndexingService.ChunkIndexRecord
        {
            Id = "chunk-make-only",
            Category = "",
            Make = "Kawasaki",
            Model = null,
            Content = "Engine specs"
        };
        var cache = new Dictionary<string, MotorcycleCategory>();
        _categoryClassifierMock
            .Setup(x => x.ResolveCategoryAsync("Kawasaki", "", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MotorcycleCategory.Sport);

        var result = await sut.ResolveChunkCategoryAsync(chunk, cache, CancellationToken.None);

        result.Should().Be(MotorcycleCategory.Sport);
    }

    // ---- ResolveChunkCategoryAsync - model-only (no make) input ----

    [Fact]
    public async Task ResolveChunkCategoryAsync_WithModelButNoMake_ShouldUseClassifier()
    {
        var sut = CreateSut();
        var chunk = new ChunkIndexingService.ChunkIndexRecord
        {
            Id = "chunk-model-only",
            Category = "",
            Make = null,
            Model = "Ninja",
            Content = "Engine specs"
        };
        var cache = new Dictionary<string, MotorcycleCategory>();
        _categoryClassifierMock
            .Setup(x => x.ResolveCategoryAsync("", "Ninja", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MotorcycleCategory.Sport);

        var result = await sut.ResolveChunkCategoryAsync(chunk, cache, CancellationToken.None);

        result.Should().Be(MotorcycleCategory.Sport);
    }

    // ---- IndexBatchForCategoryAsync success path (exercises Polly
    //      closure <>c__DisplayClass10_1 normal branch) ----

    [Fact]
    public async Task IndexFromJsonlAsync_WhenUploadSucceeds_ShouldReturnSuccessfulOutcomes()
    {
        // Use a real resilience pipeline so the Polly closure executes.
        var realPipeline = new SearchIndexResiliencePipelineProvider();

        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns((MotorcycleCategory c) => $"motorcycle-{c.Value}");
        _clientFactoryMock
            .Setup(x => x.IndexExistsAsync(MotorcycleCategory.Sport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Build a success result for MergeOrUploadDocumentsAsync
        var indexResult = CreateIndexDocumentsResult(("chunk-success", true, 201, null));
        var mockRawResponse = new Mock<global::Azure.Response>();
        var azureResponse = global::Azure.Response.FromValue(indexResult, mockRawResponse.Object);

        var searchClientMock = new Mock<SearchClient>();
        searchClientMock
            .Setup(c => c.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<ChunkIndexingService.ChunkIndexRecord>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(azureResponse);
        _clientFactoryMock.Setup(x => x.GetClient(MotorcycleCategory.Sport))
            .Returns(searchClientMock.Object);

        var sut = new ChunkIndexingService(
            _clientFactoryMock.Object,
            _categoryClassifierMock.Object,
            realPipeline,
            TestHelpers.OptionsFor(_searchOptions),
            TestHelpers.CreateNullLogger<ChunkIndexingService>());

        var jsonl = "{\"id\":\"chunk-success\",\"category\":\"sport\",\"make\":\"Honda\",\"model\":\"CBR600RR\",\"content\":\"test\",\"title\":\"t\",\"documentType\":\"manual\",\"contentVector\":[0.1,0.2]}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));

        var result = await sut.IndexFromJsonlAsync(stream, Guid.NewGuid().ToString(), Guid.Empty, Guid.Empty, null, CancellationToken.None);

        result.TotalParsed.Should().Be(1);
        result.Outcomes.Should().NotBeEmpty();
        var outcome = result.Outcomes[0];
        outcome.Succeeded.Should().BeTrue();
        outcome.ChunkId.Should().Be("chunk-success");
    }

    [Fact]
    public async Task IndexFromJsonlAsync_WhenOptionalRecordFieldsAreMissing_UsesSafeSchemaDefaults()
    {
        // Arrange
        var realPipeline = new SearchIndexResiliencePipelineProvider();
        ChunkIndexingService.ChunkIndexRecord? indexedRecord = null;
        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");
        _clientFactoryMock
            .Setup(x => x.IndexExistsAsync(MotorcycleCategory.Sport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var indexResult = CreateIndexDocumentsResult(("chunk-defaults", true, 201, null));
        var response = global::Azure.Response.FromValue(indexResult, new Mock<global::Azure.Response>().Object);
        var searchClientMock = new Mock<SearchClient>();
        searchClientMock
            .Setup(client => client.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<ChunkIndexingService.ChunkIndexRecord>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<ChunkIndexingService.ChunkIndexRecord> documents, IndexDocumentsOptions _, CancellationToken _) =>
                indexedRecord = documents.Should().ContainSingle().Subject)
            .ReturnsAsync(response);
        _clientFactoryMock.Setup(x => x.GetClient(MotorcycleCategory.Sport)).Returns(searchClientMock.Object);
        var sut = new ChunkIndexingService(
            _clientFactoryMock.Object,
            _categoryClassifierMock.Object,
            realPipeline,
            TestHelpers.OptionsFor(_searchOptions),
            TestHelpers.CreateNullLogger<ChunkIndexingService>());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":\"chunk-defaults\",\"category\":\"sport\",\"content\":\"content\"}"));

        // Act
        await sut.IndexFromJsonlAsync(stream, "upload-defaults", Guid.Empty, Guid.Empty, null);

        // Assert
        indexedRecord.Should().NotBeNull();
        indexedRecord!.Title.Should().BeEmpty();
        indexedRecord.DocumentType.Should().BeEmpty();
        indexedRecord.Make.Should().BeNull();
        indexedRecord.Model.Should().BeNull();
        indexedRecord.Year.Should().Be(0);
        indexedRecord.SourceFile.Should().BeNull();
        indexedRecord.Section.Should().BeNull();
        indexedRecord.PageNumber.Should().Be(0);
        indexedRecord.PageRange.Should().BeNull();
        indexedRecord.PrimarySection.Should().BeNull();
        indexedRecord.SectionLevel.Should().Be(0);
        indexedRecord.SectionHeadings.Should().BeEmpty();
        indexedRecord.TableCaption.Should().BeNull();
        indexedRecord.ChunkIndex.Should().Be(0);
        indexedRecord.Tags.Should().BeEmpty();
        indexedRecord.ContentVector.Should().BeEmpty();
        indexedRecord.CreatedAt.Should().Be(default);
        indexedRecord.UpdatedAt.Should().Be(default);
    }

    private static IndexDocumentsResult CreateIndexDocumentsResult(
        params (string Key, bool Succeeded, int Status, string? ErrorMessage)[] items)
    {
        var valueItems = items.Select(i =>
        {
            if (i.Succeeded)
                return $$"""{"key":"{{i.Key}}","status":true,"statusCode":{{i.Status}}}""";
            var errMsg = i.ErrorMessage ?? "Error";
            return $$"""{"key":"{{i.Key}}","status":false,"statusCode":{{i.Status}},"errorMessage":"{{errMsg}}"}""";
        });
        var json = $$"""{"value":[{{string.Join(",", valueItems)}}]}""";
        return ModelReaderWriter.Read<IndexDocumentsResult>(BinaryData.FromString(json))!;
    }

    // ---- Helpers ----

    // ─── DisplayClass10_1 cancellation branch ─────────────────────────
    // Covers the <>c__DisplayClass10_1 closure where linkedToken.ThrowIfCancellationRequested()
    // is called. When the external cancellation token is already cancelled, the
    // throw-from-lambda path is exercised through the Polly retry pipeline.

    [Fact]
    public async Task IndexFromJsonlAsync_WhenExternalTokenAlreadyCancelled_ShouldThrowOperationCanceledException()
    {
        var realPipeline = new SearchIndexResiliencePipelineProvider();

        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns((MotorcycleCategory c) => $"motorcycle-{c.Value}");
        _clientFactoryMock
            .Setup(x => x.IndexExistsAsync(MotorcycleCategory.Sport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var searchClientMock = new Mock<SearchClient>();
        searchClientMock
            .Setup(c => c.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<ChunkIndexingService.ChunkIndexRecord>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated batch failure"));
        _clientFactoryMock.Setup(x => x.GetClient(MotorcycleCategory.Sport))
            .Returns(searchClientMock.Object);

        var sut = new ChunkIndexingService(
            _clientFactoryMock.Object,
            _categoryClassifierMock.Object,
            realPipeline,
            TestHelpers.OptionsFor(_searchOptions),
            TestHelpers.CreateNullLogger<ChunkIndexingService>());

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var jsonl = "{\"id\":\"chunk-cancel\",\"category\":\"sport\",\"content\":\"test\",\"title\":\"t\",\"documentType\":\"manual\",\"contentVector\":[0.1,0.2]}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));

        var act = async () => await sut.IndexFromJsonlAsync(stream, "upload-cancel-test", Guid.Empty, Guid.Empty, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─── IndexBatchForCategoryAsync timeout path ──────────────────────
    // Covers the per-batch timeout branch of IndexBatchForCategoryAsync (T7) where
    // the batchIndexTimeout fires and OperationCanceledException is caught, then
    // rethrown as TimeoutException.

    [Fact]
    public async Task IndexFromJsonlAsync_WhenBatchTimeoutFires_ShouldThrowTimeoutException()
    {
        var realPipeline = new SearchIndexResiliencePipelineProvider();
        // Set a very short timeout (1ms) so the per-batch timeout fires before
        // any real work completes
        var shortTimeoutOptions = new SearchOptions
        {
            MaxSearchResults = 50,
            BatchSize = 100,
            BatchIndexTimeoutSeconds = 1,
            IndexName = "motorcycle-sport"
        };

        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns((MotorcycleCategory c) => $"motorcycle-{c.Value}");
        _clientFactoryMock
            .Setup(x => x.IndexExistsAsync(MotorcycleCategory.Sport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Mock MergeOrUploadDocumentsAsync to delay beyond the 1s timeout
        var searchClientMock = new Mock<SearchClient>();
        searchClientMock
            .Setup(c => c.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<ChunkIndexingService.ChunkIndexRecord>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (IEnumerable<ChunkIndexingService.ChunkIndexRecord> docs, IndexDocumentsOptions opts, CancellationToken token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                throw new OperationCanceledException();
            });
        _clientFactoryMock.Setup(x => x.GetClient(MotorcycleCategory.Sport))
            .Returns(searchClientMock.Object);

        var sut = new ChunkIndexingService(
            _clientFactoryMock.Object,
            _categoryClassifierMock.Object,
            realPipeline,
            TestHelpers.OptionsFor(shortTimeoutOptions),
            TestHelpers.CreateNullLogger<ChunkIndexingService>());

        var jsonl = "{\"id\":\"chunk-timeout\",\"category\":\"sport\",\"content\":\"test\",\"title\":\"t\",\"documentType\":\"manual\",\"contentVector\":[0.1,0.2]}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));

        var act = async () => await sut.IndexFromJsonlAsync(stream, "upload-timeout-test", Guid.Empty, Guid.Empty, null, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*timed out*");
    }


    [Theory]
    [InlineData(typeof(IChunkIndexingService))]
    [InlineData(typeof(ChunkIndexingService))]
    [InlineData(typeof(InMemorySearchShimChunkIndexingService))]
    public void IndexFromJsonlAsync_ShouldExposeExactlyOneAnchorParameterOverload_OnInterfaceAndImplementations(Type type)
    {
        // T14 (plan §5/§10b2): the contract must declare EXACTLY ONE IndexFromJsonlAsync method,
        // shaped (Stream jsonlStream, string uploadId, Guid indexedArtifactId, Guid ingestionJobId,
        // string sourceContentHash, CancellationToken ct = default), on IChunkIndexingService and
        // both implementations. Removing the transitional 3-parameter overload closes the §7
        // null-clobber footgun permanently: no future caller can silently omit anchors and re-open
        // the class of bug where mergeOrUpload treated an explicit-null anchor as "clear this field".
        // RED until T14 deletes the legacy 3-parameter overload from all three types.
        var overloads = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(IChunkIndexingService.IndexFromJsonlAsync))
            .ToList();

        overloads.Should().HaveCount(1,
            $"{type.FullName} must declare exactly one IndexFromJsonlAsync method after T14 contract closure — " +
            "a second (e.g. legacy 3-parameter) overload would re-open the §7 null-clobber footgun. " +
            "Found: {0}", string.Join(", ", overloads.Select(o => $"({string.Join(", ", o.GetParameters().Select(p => p.ParameterType.Name))})")));

        var sole = overloads.Single();
        var p = sole.GetParameters();
        p.Length.Should().Be(6,
            $"{type.FullName}.IndexFromJsonlAsync must take 6 parameters (Stream, string, Guid, Guid, string?, CancellationToken)");
        p[0].ParameterType.Should().Be<Stream>();
        p[1].ParameterType.Should().Be<string>();
        p[2].ParameterType.Should().Be<Guid>();
        p[3].ParameterType.Should().Be<Guid>();
        p[4].ParameterType.Should().Be<string>(
            $"{type.FullName}.IndexFromJsonlAsync parameter 5 must be string (sourceContentHash) — " +
            "the anchor contract requires it (plan D3/T7)");
        p[5].ParameterType.Should().Be<CancellationToken>();

        // The parameter names themselves are part of the contract (plan §5 T14: "indexedArtifactId,
        // ingestionJobId, sourceContentHash"). Assert them so a rename can't silently break callers
        // that pass the anchors by name.
        p[2].Name.Should().Be("indexedArtifactId");
        p[3].Name.Should().Be("ingestionJobId");
        p[4].Name.Should().Be("sourceContentHash");
    }

    [Fact]
    public async Task IndexFromJsonlAsync_WhenCalledWithAnchorParameters_ShouldStampEveryDocumentWithIndexedArtifactIdIngestionJobIdAndSourceContentHash()
    {
        // D3/T7: given a JSONL stream with 2 chunk records, every document in the batch sent to
        // SearchClient.MergeOrUploadDocumentsAsync must carry indexedArtifactId/ingestionJobId
        // (as strings matching the passed GUIDs) and sourceContentHash == "h1". Captured via
        // Moq's argument capture on the mocked SearchClient — not inferred from the return value.

        var realPipeline = new SearchIndexResiliencePipelineProvider();
        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns((MotorcycleCategory c) => $"motorcycle-{c.Value}");
        _clientFactoryMock
            .Setup(x => x.IndexExistsAsync(MotorcycleCategory.Sport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var indexResult = CreateIndexDocumentsResult(
            ("chunk-anchor-1", true, 201, null),
            ("chunk-anchor-2", true, 201, null));
        var response = global::Azure.Response.FromValue(indexResult, new Mock<global::Azure.Response>().Object);

        IReadOnlyList<ChunkIndexingService.ChunkIndexRecord>? capturedBatch = null;
        var searchClientMock = new Mock<SearchClient>();
        searchClientMock
            .Setup(c => c.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<ChunkIndexingService.ChunkIndexRecord>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<ChunkIndexingService.ChunkIndexRecord> documents, IndexDocumentsOptions _, CancellationToken _) =>
                capturedBatch = documents.ToList())
            .ReturnsAsync(response);
        _clientFactoryMock.Setup(x => x.GetClient(MotorcycleCategory.Sport)).Returns(searchClientMock.Object);

        var sut = new ChunkIndexingService(
            _clientFactoryMock.Object,
            _categoryClassifierMock.Object,
            realPipeline,
            TestHelpers.OptionsFor(_searchOptions),
            TestHelpers.CreateNullLogger<ChunkIndexingService>());

        var jsonl =
            "{\"id\":\"chunk-anchor-1\",\"category\":\"sport\",\"content\":\"c1\",\"title\":\"t1\",\"documentType\":\"manual\",\"contentVector\":[0.1,0.2]}\n" +
            "{\"id\":\"chunk-anchor-2\",\"category\":\"sport\",\"content\":\"c2\",\"title\":\"t2\",\"documentType\":\"manual\",\"contentVector\":[0.3,0.4]}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));

        var indexedArtifactId = Guid.NewGuid();
        var ingestionJobId = Guid.NewGuid();
        const string sourceContentHash = "h1";

        await sut.IndexFromJsonlAsync(stream, "upload-anchor-test", indexedArtifactId, ingestionJobId, sourceContentHash, CancellationToken.None);

        capturedBatch.Should().NotBeNull(
            "the mocked SearchClient.MergeOrUploadDocumentsAsync must be invoked with the parsed batch");
        capturedBatch!.Should().HaveCount(2);

        foreach (var document in capturedBatch!)
        {
            var documentType = document!.GetType();
            var indexedArtifactIdValue = documentType.GetProperty("IndexedArtifactId")?.GetValue(document) as string;
            var ingestionJobIdValue = documentType.GetProperty("IngestionJobId")?.GetValue(document) as string;
            var sourceContentHashValue = documentType.GetProperty("SourceContentHash")?.GetValue(document) as string;

            indexedArtifactIdValue.Should().Be(
                indexedArtifactId.ToString(),
                "every stamped document must carry the passed indexedArtifactId as a string (plan decision D3, T7)");
            ingestionJobIdValue.Should().Be(
                ingestionJobId.ToString(),
                "every stamped document must carry the passed ingestionJobId as a string (plan decision D3, T7)");
            sourceContentHashValue.Should().Be(
                sourceContentHash,
                "every stamped document must carry the passed sourceContentHash (plan decision D3, T7)");
        }
    }

    [Fact]
    public void ChunkIndexRecord_WhenAnchorValuesAreNull_ShouldOmitThemFromSerializedJson()
    {
        // D3/T7: When anchor values are null (unset), they must be omitted from the serialized JSON
        // entirely, not written as explicit `null`. This prevents the legacy 3-parameter overload
        // (which delegates with Guid.Empty/empty string → null) from wiping anchors off already-indexed
        // documents via mergeOrUpload.
        using var jsonStream = new MemoryStream();
        using var writer = new System.Text.Json.Utf8JsonWriter(jsonStream);

        var record = new ChunkIndexingService.ChunkIndexRecord
        {
            Id = "chunk-1",
            Title = "Test",
            Content = "Content",
            DocumentType = "manual",
            Category = "sport",
            ContentVector = new[] { 0.1f, 0.2f },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IndexedArtifactId = null,      // Unset → should be omitted
            IngestionJobId = null,         // Unset → should be omitted
            SourceContentHash = null       // Unset → should be omitted
        };

        System.Text.Json.JsonSerializer.Serialize(writer, record);
        writer.Flush();

        var json = Encoding.UTF8.GetString(jsonStream.ToArray());
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Assert: the three anchor fields must NOT be present in the JSON
        root.TryGetProperty("indexedArtifactId", out _).Should().BeFalse(
            "null anchor fields must be omitted from JSON entirely (prevents merge-related anchor wipe)");
        root.TryGetProperty("ingestionJobId", out _).Should().BeFalse(
            "null anchor fields must be omitted from JSON entirely (prevents merge-related anchor wipe)");
        root.TryGetProperty("sourceContentHash", out _).Should().BeFalse(
            "null anchor fields must be omitted from JSON entirely (prevents merge-related anchor wipe)");

        // Verify that present fields are still there
        root.TryGetProperty("id", out var idElem).Should().BeTrue();
        idElem.GetString().Should().Be("chunk-1");
    }

    private static MemoryStream CreateJsonlStream(params (string id, string category)[] chunks)
    {
        var lines = new List<string>();
        foreach (var (id, category) in chunks)
        {
            var record = $"{{\"id\":\"{id}\",\"category\":\"{category}\",\"content\":\"content for {id}\",\"title\":\"title\",\"documentType\":\"manual\",\"contentVector\":[0.1,0.2]}}";
            lines.Add(record);
        }
        return new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
    }
}
