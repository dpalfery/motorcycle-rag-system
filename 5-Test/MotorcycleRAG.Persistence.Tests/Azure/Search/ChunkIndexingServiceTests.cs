using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.ValueObjects;
using MotorcycleRAG.Persistence.Azure.Search;

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

        var act = async () => await sut.IndexFromJsonlAsync(null!, "upload-1");

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

        var act = async () => await sut.IndexFromJsonlAsync(stream, uploadId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("uploadId");
    }

    // ---- IndexFromJsonlAsync - empty stream ----

    [Fact]
    public async Task IndexFromJsonlAsync_WithEmptyStream_ShouldReturnZeroOutcomes()
    {
        var sut = CreateSut();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(""));

        var result = await sut.IndexFromJsonlAsync(stream, "upload-empty");

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

        var result = await sut.IndexFromJsonlAsync(stream, "upload-skip");

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

        var result = await sut.IndexFromJsonlAsync(stream, "upload-blank-lines");

        // Only 2 valid records should be parsed, blank lines skipped
        result.TotalParsed.Should().Be(2);
    }

    // ---- Helpers ----

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
