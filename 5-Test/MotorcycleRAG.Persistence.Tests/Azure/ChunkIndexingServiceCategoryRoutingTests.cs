using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.ValueObjects;
using MotorcycleRAG.Persistence.Azure.Search;

namespace MotorcycleRAG.UnitTests.Azure;

/// <summary>
/// Unit tests for <see cref="ChunkIndexingService"/>'s category routing (T4 acceptance
/// criterion #1: "Chunks for a Dirt bike are written to the Dirt index only"). Exercises the
/// pure routing/grouping decision with the classifier and factory mocked &mdash; no Azure SDK
/// call is made and the in-memory shim is not used.
/// </summary>
public class ChunkIndexingServiceCategoryRoutingTests
{
    private readonly Mock<IMotorcycleCategoryClassifier> _classifier = new();
    private readonly Mock<ISearchClientFactory> _factory = new();
    private readonly ChunkIndexingService _sut;

    public ChunkIndexingServiceCategoryRoutingTests()
    {
        _factory.SetupGet(f => f.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _factory.Setup(f => f.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns<MotorcycleCategory>(c => $"motorcycle-{c.Value}");

        _sut = new ChunkIndexingService(
            _factory.Object,
            _classifier.Object,
            Mock.Of<ISearchIndexResiliencePipeline>(),
            Microsoft.Extensions.Options.Options.Create(new MotorcycleRAG.Core.Options.SearchOptions()),
            NullLogger<ChunkIndexingService>.Instance);
    }

    private static ChunkIndexingService.ChunkIndexRecord Chunk(
        string id,
        string? category = null,
        string? make = null,
        string? model = null) => new()
        {
            Id = id,
            Category = category ?? string.Empty,
            Make = make,
            Model = model,
            ContentVector = [0.1f, 0.2f]
        };

    // ----------------------------------------------------------------
    // Acceptance criterion #1: Dirt chunks -> Dirt index ONLY.
    // ----------------------------------------------------------------

    [Fact]
    public async Task EmbeddedDirtChunks_GroupUnderDirtOnly_NoClassifierCall()
    {
        var chunks = new[]
        {
            Chunk("d1", category: "dirt", make: "KTM", model: "350 SX-F"),
            Chunk("d2", category: "dirt", make: "Honda", model: "CRF450R")
        };

        var groups = await _sut.GroupByCategoryAsync(chunks, CancellationToken.None);

        groups.Should().ContainSingle().Which.Key.Should().Be(MotorcycleCategory.Dirt);
        groups[MotorcycleCategory.Dirt].Should().HaveCount(2);
        _classifier.Verify(
            c => c.ResolveCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an embedded category must short-circuit the classifier (D7 operator override)");
    }

    [Theory]
    [InlineData("dirt", nameof(MotorcycleCategory.Dirt))]
    [InlineData("touring", nameof(MotorcycleCategory.Touring))]
    [InlineData("sport", nameof(MotorcycleCategory.Sport))]
    [InlineData("cruiser", nameof(MotorcycleCategory.Cruiser))]
    public async Task EmbeddedCategory_RoutesToMatchingPartition(string wireValue, string expectedName)
    {
        var expected = MotorcycleCategory.Parse(expectedName.ToLowerInvariant());

        var groups = await _sut.GroupByCategoryAsync(
            new[] { Chunk("c1", category: wireValue) },
            CancellationToken.None);

        groups.Should().ContainSingle().Which.Key.Should().Be(expected);
    }

    // ----------------------------------------------------------------
    // Classifier fallback: no embedded category -> resolve (make, model), cached per bike.
    // ----------------------------------------------------------------

    [Fact]
    public async Task MissingCategory_ResolvesViaClassifier_CachedPerBike()
    {
        _classifier.Setup(c => c.ResolveCategoryAsync("Honda", "CRF450R", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MotorcycleCategory.Dirt);

        var chunks = new[]
        {
            Chunk("a", make: "Honda", model: "CRF450R"),
            Chunk("b", make: "Honda", model: "CRF450R"), // same bike -> cache hit
            Chunk("c", make: "Honda", model: "CRF450R")
        };

        var groups = await _sut.GroupByCategoryAsync(chunks, CancellationToken.None);

        groups.Should().ContainSingle().Which.Key.Should().Be(MotorcycleCategory.Dirt);
        groups[MotorcycleCategory.Dirt].Should().HaveCount(3);
        _classifier.Verify(
            c => c.ResolveCategoryAsync("Honda", "CRF450R", It.IsAny<CancellationToken>()),
            Times.Once,
            "the same (make, model) must classify at most once");
    }

    [Fact]
    public async Task MixedEmbeddedAndResolved_ProduceSeparateHomogeneousGroups()
    {
        _classifier.Setup(c => c.ResolveCategoryAsync("BMW", "R1250RT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MotorcycleCategory.Touring);

        var chunks = new[]
        {
            Chunk("d1", category: "dirt", make: "KTM", model: "350 SX-F"),
            Chunk("d2", category: "dirt", make: "Husqvarna", model: "FC 450"),
            Chunk("t1", make: "BMW", model: "R1250RT") // no embedded category -> classifier -> Touring
        };

        var groups = await _sut.GroupByCategoryAsync(chunks, CancellationToken.None);

        groups.Should().HaveCount(2);
        groups[MotorcycleCategory.Dirt].Should().HaveCount(2);
        groups[MotorcycleCategory.Touring].Should().ContainSingle();
        // The embedded Dirt bikes must never invoke the classifier.
        _classifier.Verify(
            c => c.ResolveCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Last-resort fallback: no category and no make/model -> default partition.
    // ----------------------------------------------------------------

    [Fact]
    public async Task NoCategoryAndNoBike_FallsBackToDefaultPartition()
    {
        var groups = await _sut.GroupByCategoryAsync(
            new[] { Chunk("orphan") },
            CancellationToken.None);

        groups.Should().ContainSingle().Which.Key.Should().Be(MotorcycleCategory.Sport);
        _classifier.Verify(
            c => c.ResolveCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "without make/model the classifier cannot be invoked");
    }

    // ----------------------------------------------------------------
    // Undefined embedded category (e.g. "naked") is treated as missing -> classifier path.
    // ----------------------------------------------------------------

    [Fact]
    public async Task InvalidEmbeddedCategory_FallsThroughToClassifier()
    {
        _classifier.Setup(c => c.ResolveCategoryAsync("Suzuki", "GSX-R1000", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MotorcycleCategory.Sport);

        var groups = await _sut.GroupByCategoryAsync(
            new[] { Chunk("s1", category: "naked", make: "Suzuki", model: "GSX-R1000") },
            CancellationToken.None);

        groups.Should().ContainSingle().Which.Key.Should().Be(MotorcycleCategory.Sport);
    }
}
