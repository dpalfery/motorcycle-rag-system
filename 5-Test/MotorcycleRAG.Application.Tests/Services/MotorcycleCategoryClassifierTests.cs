using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs.Graph;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="MotorcycleCategoryClassifier"/> covering the D7 acceptance criteria:
/// cache hit (no LLM), cache miss (LLM once + write-back, then hit), and LLM-unreachable/parsable fallback
/// (never throws, never poisons the cache).
/// </summary>
/// <remarks>
/// The LLM client, the authoritative cache repository, and the graph repository are all mocked. These
/// tests do NOT use the in-memory Search shim — the classifier has no Search dependency.
/// </remarks>
public class MotorcycleCategoryClassifierTests
{
    private readonly Mock<IBikeModelCategoryRepository> _categoryRepo = new();
    private readonly Mock<ILocalChatClient> _chatClient = new();
    private readonly Mock<IGraphRepository> _graphRepo = new();
    private readonly IOptions<ClassifierOptions> _options = Options.Create(new ClassifierOptions());
    private readonly ILogger<MotorcycleCategoryClassifier> _logger =
        NullLogger<MotorcycleCategoryClassifier>.Instance;

    private MotorcycleCategoryClassifier CreateSut() => new(
        _categoryRepo.Object,
        _chatClient.Object,
        _graphRepo.Object,
        _options,
        _logger);

    // ----------------------------------------------------------------
    // Acceptance criterion 2: cache hit returns stored category without calling the LLM.
    // ----------------------------------------------------------------

    [Fact]
    public async Task CacheHit_ReturnsStoredCategory_WithoutCallingLlm()
    {
        // Arrange: authoritative cache holds "Sport" (DB stores Title-case; the value object parses
        // case-insensitively back to the lowercase canonical).
        _categoryRepo
            .Setup(r => r.GetCategoryAsync("Honda", "CBR 1000RR", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Sport");

        var sut = CreateSut();

        // Act
        var result = await sut.ResolveCategoryAsync("Honda", "CBR 1000RR");

        // Assert
        result.Should().Be(MotorcycleCategory.Sport);

        _categoryRepo.Verify(
            r => r.GetCategoryAsync("Honda", "CBR 1000RR", It.IsAny<CancellationToken>()),
            Times.Once);
        _chatClient.Verify(
            c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _categoryRepo.Verify(
            r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _graphRepo.Verify(
            r => r.UpsertNodeAsync(It.IsAny<GraphNodeDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("Sport", nameof(MotorcycleCategory.Sport))]
    [InlineData("sport", nameof(MotorcycleCategory.Sport))]
    [InlineData("DIRT", nameof(MotorcycleCategory.Dirt))]
    [InlineData("Touring", nameof(MotorcycleCategory.Touring))]
    public async Task CacheHit_ParsesStoredValueCaseInsensitively(string stored, string expected)
    {
        // The DB stores Title-case ('Sport'); the value object must parse any casing back to canonical.
        _categoryRepo
            .Setup(r => r.GetCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var sut = CreateSut();

        var result = await sut.ResolveCategoryAsync("Honda", "CBR 1000RR");

        result.Value.Should().Be(expected.ToLowerInvariant());
        _chatClient.Verify(
            c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CacheHit_NormalizesMakeAndModelBeforeLookup()
    {
        // Equivalent spellings must resolve to the same cache row.
        const string stored = "dirt";
        _categoryRepo
            .Setup(r => r.GetCategoryAsync("Honda", "CBR 1000RR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var sut = CreateSut();

        var result = await sut.ResolveCategoryAsync("honda", "cbr 1000rr");

        result.Should().Be(MotorcycleCategory.Dirt);
        _categoryRepo.Verify(
            r => r.GetCategoryAsync("Honda", "CBR 1000RR", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Acceptance criterion 3: cache miss calls the LLM exactly once, then writes the table row;
    // a second call is a cache hit.
    // ----------------------------------------------------------------

    [Fact]
    public async Task CacheMiss_CallsLlmOnce_WritesCache_AndGraphWriteThroughOccurs()
    {
        // Arrange: cache miss on the first read.
        _categoryRepo
            .SetupSequence(r => r.GetCategoryAsync("Honda", "CBR 1000RR", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);   // first call only — second call in the dedicated test below

        _chatClient
            .Setup(c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Sport");

        var sut = CreateSut();

        // Act
        var result = await sut.ResolveCategoryAsync("Honda", "CBR 1000RR");

        // Assert: LLM-driven category returned
        result.Should().Be(MotorcycleCategory.Sport);

        // LLM called exactly once
        _chatClient.Verify(
            c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Table row written (lowercase canonical value passed; repo maps to DB storage form)
        _categoryRepo.Verify(
            r => r.UpsertAsync("Honda", "CBR 1000RR", "sport", "Classifier", It.IsAny<CancellationToken>()),
            Times.Once);

        // Graph write-through: Motorcycle node + Category node + BELONGS_TO edge
        _graphRepo.Verify(
            r => r.UpsertNodeAsync(It.Is<GraphNodeDto>(n => n.Type == "Motorcycle" && n.Name == "Honda CBR 1000RR"), It.IsAny<CancellationToken>()),
            Times.Once);
        _graphRepo.Verify(
            r => r.UpsertNodeAsync(It.Is<GraphNodeDto>(n => n.Type == "Category" && n.Name == "sport"), It.IsAny<CancellationToken>()),
            Times.Once);
        _graphRepo.Verify(
            r => r.UpsertEdgeAsync(It.Is<GraphEdgeDto>(e => e.RelationshipType == "BELONGS_TO"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CacheMiss_SecondCallForSameKey_IsCacheHit_NoSecondLlmCall()
    {
        // First call: miss -> LLM -> write-back. Second call: the cache now holds the value -> hit.
        _categoryRepo
            .SetupSequence(r => r.GetCategoryAsync("Kawasaki", "NINJA 400", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null)   // 1st read: miss
            .ReturnsAsync("Sport");        // 2nd read: hit (the write-back would have stored this)

        _chatClient
            .Setup(c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Sport");

        var sut = CreateSut();

        // First call: miss path
        var first = await sut.ResolveCategoryAsync("Kawasaki", "Ninja 400");
        first.Should().Be(MotorcycleCategory.Sport);

        // Second call: hit path
        var second = await sut.ResolveCategoryAsync("Kawasaki", "Ninja 400");
        second.Should().Be(MotorcycleCategory.Sport);

        // LLM called exactly ONCE across both calls (the second call was a cache hit)
        _chatClient.Verify(
            c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Cache read called twice (once per ResolveCategoryAsync)
        _categoryRepo.Verify(
            r => r.GetCategoryAsync("Kawasaki", "NINJA 400", It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Write-back called exactly once (on the miss)
        _categoryRepo.Verify(
            r => r.UpsertAsync("Kawasaki", "NINJA 400", "sport", "Classifier", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Acceptance criterion 4: LLM unreachable / unparsable -> fallback, no unhandled throw.
    // ----------------------------------------------------------------

    [Fact]
    public async Task LlmUnreachable_ReturnsFallback_DoesNotThrow_DoesNotWriteCache()
    {
        // Cache miss + LLM transport failure.
        _categoryRepo
            .Setup(r => r.GetCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _chatClient
            .Setup(c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var sut = CreateSut();

        // Act + Assert: does NOT throw (capture result from the single call so we don't double-invoke).
        MotorcycleCategory result = default;
        Func<Task> act = async () => result = await sut.ResolveCategoryAsync("Honda", "CBR 1000RR");
        await act.Should().NotThrowAsync();

        // Returns the configured fallback (Sport by default)
        result.Should().Be(MotorcycleCategory.Sport);

        // Critical: fallback MUST NOT poison the authoritative cache (R4).
        _categoryRepo.Verify(
            r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _graphRepo.Verify(
            r => r.UpsertNodeAsync(It.IsAny<GraphNodeDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LlmReturnsUnparsable_ReturnsFallback_DoesNotThrow_DoesNotWriteCache()
    {
        _categoryRepo
            .Setup(r => r.GetCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _chatClient
            .Setup(c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("banana");   // not a valid category

        var sut = CreateSut();

        MotorcycleCategory result = default;
        Func<Task> act = async () => result = await sut.ResolveCategoryAsync("Honda", "CBR 1000RR");
        await act.Should().NotThrowAsync();

        result.Should().Be(MotorcycleCategory.Sport);

        _categoryRepo.Verify(
            r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LlmReturnsSentence_ParsesCategoryTokenCaseInsensitively()
    {
        // The LLM is instructed to return only the token, but it sometimes returns a sentence.
        // The parser must extract the category word.
        _categoryRepo
            .Setup(r => r.GetCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _chatClient
            .Setup(c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("The Honda CBR1000RR is a Sport motorcycle.");

        var sut = CreateSut();

        var result = await sut.ResolveCategoryAsync("Honda", "CBR 1000RR");

        result.Should().Be(MotorcycleCategory.Sport);
        _categoryRepo.Verify(
            r => r.UpsertAsync("Honda", "CBR 1000RR", "sport", "Classifier", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Resilience: best-effort write-backs must not fail the classification.
    // ----------------------------------------------------------------

    [Fact]
    public async Task CacheWriteBackFailure_DoesNotFailClassification()
    {
        _categoryRepo
            .Setup(r => r.GetCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _categoryRepo
            .Setup(r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB down"));

        _chatClient
            .Setup(c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("dirt");

        var sut = CreateSut();

        MotorcycleCategory result = default;
        Func<Task> act = async () => result = await sut.ResolveCategoryAsync("Yamaha", "YZ 250");
        await act.Should().NotThrowAsync();

        result.Should().Be(MotorcycleCategory.Dirt);
    }

    [Fact]
    public async Task GraphWriteThroughFailure_DoesNotFailClassification()
    {
        _categoryRepo
            .Setup(r => r.GetCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _chatClient
            .Setup(c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("touring");

        _graphRepo
            .Setup(r => r.UpsertNodeAsync(It.IsAny<GraphNodeDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph edge table locked"));

        var sut = CreateSut();

        MotorcycleCategory result = default;
        Func<Task> act = async () => result = await sut.ResolveCategoryAsync("BMW", "R 1250 RT");
        await act.Should().NotThrowAsync();

        result.Should().Be(MotorcycleCategory.Touring);

        // Cache is still authoritative — the row was written even though the graph projection failed.
        _categoryRepo.Verify(
            r => r.UpsertAsync("Bmw", "R 1250 RT", "touring", "Classifier", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Deterministic graph node Ids (idempotent upserts).
    // ----------------------------------------------------------------

    [Fact]
    public async Task GraphWriteThrough_UsesDeterministicNodeIds()
    {
        _categoryRepo
            .Setup(r => r.GetCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _chatClient
            .Setup(c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("cruiser");

        // Capture the bike node Ids the classifier emits across two classifications of the SAME bike.
        var capturedBikeNodeIds = new List<Guid>();
        _graphRepo
            .Setup(r => r.UpsertNodeAsync(It.IsAny<GraphNodeDto>(), It.IsAny<CancellationToken>()))
            .Callback<GraphNodeDto, CancellationToken>((node, _) =>
            {
                if (node.Type == "Motorcycle")
                {
                    capturedBikeNodeIds.Add(node.Id);
                }
            })
            .Returns(Task.CompletedTask);

        var sut = CreateSut();

        await sut.ResolveCategoryAsync("Harley-Davidson", "Sportster");
        await sut.ResolveCategoryAsync("Harley-Davidson", "Sportster");

        // Same (Type, Name) MUST produce the same Id so the graph upsert is idempotent.
        capturedBikeNodeIds.Should().HaveCount(2);
        capturedBikeNodeIds[0].Should().Be(capturedBikeNodeIds[1]);
    }

    [Fact]
    public async Task FallbackCategory_IsConfigurable()
    {
        // Reconfigure the fallback to Cruiser and verify it is honored on LLM failure.
        var customOptions = Options.Create(new ClassifierOptions { FallbackCategory = "cruiser" });
        _categoryRepo
            .Setup(r => r.GetCategoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _chatClient
            .Setup(c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        var sut = new MotorcycleCategoryClassifier(
            _categoryRepo.Object, _chatClient.Object, _graphRepo.Object, customOptions, _logger);

        var result = await sut.ResolveCategoryAsync("Honda", "Gold Wing");

        result.Should().Be(MotorcycleCategory.Cruiser);
    }
}
