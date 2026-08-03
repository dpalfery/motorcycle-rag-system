using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;
using MotorcycleRAG.Domain.Entities;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

public class ChunkAnchorBackfillServiceTests
{
    /// <summary>
    /// Every <c>ChunkIndexRecord</c> field that must NOT appear in a partial-update
    /// (merge-patch) document. A merge-patch that includes <c>contentVector</c> would
    /// trigger a full re-index (the vector field is non-mergeable), and including other
    /// fields like <c>content</c> would overwrite their existing values with defaults —
    /// both are forbidden. Hoisted to a static readonly field to satisfy CA1861.
    /// </summary>
    private static readonly string[] ForbiddenFields =
    [
        // Search-index metadata
        "Id",            // allowed — this IS the anchor field (key)
        "IndexedArtifactId", // allowed — anchor
        "IngestionJobId",    // allowed — anchor
        "SourceContentHash", // allowed — anchor
        // --- everything below must be ABSENT from a merge-patch ---
        "ContentVector",
        "Content",
        "Title",
        "DocumentType",
        "Category",
        "Make",
        "Model",
        "Year",
        "SourceFile",
        "Section",
        "PageNumber",
        "PageRange",
        "PrimarySection",
        "SectionLevel",
        "SectionHeadings",
        "TableCaption",
        "ChunkIndex",
        "Tags",
        "CreatedAt",
        "UpdatedAt"
    ];

    /// <summary>The four wire-shape JSON keys a non-null partial-update must carry.</summary>
    private static readonly string[] ExpectedAnchorJsonKeys =
        ["id", "indexedArtifactId", "ingestionJobId", "sourceContentHash"];

    /// <summary>The three keys present when sourceContentHash is null (the omitted key).</summary>
    private static readonly string[] ExpectedAnchorJsonKeysWithoutHash =
        ["id", "indexedArtifactId", "ingestionJobId"];

    private static readonly HashSet<string> AnchorFieldNames =
        new(StringComparer.Ordinal) { "Id", "IndexedArtifactId", "IngestionJobId", "SourceContentHash" };

    private static readonly string[] TwoChunkIds = ["chunk-001", "chunk-002"];

    private readonly Mock<IIndexedChunkRepository> _chunkRepositoryMock;
    private readonly Mock<IManualDocumentRepository> _manualDocumentRepositoryMock;
    private readonly ChunkAnchorBackfillService _sut;

    public ChunkAnchorBackfillServiceTests()
    {
        _chunkRepositoryMock = new Mock<IIndexedChunkRepository>();
        _manualDocumentRepositoryMock = new Mock<IManualDocumentRepository>();
        _sut = new ChunkAnchorBackfillService(
            _chunkRepositoryMock.Object,
            _manualDocumentRepositoryMock.Object);
    }

    [Fact]
    public async Task BuildBackfillDocumentsAsync_WithTwoChunks_ReturnsOnePartialUpdateDocumentPerChunk()
    {
        // Arrange
        var artifactId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var sourceContentHash = "sha256-abc123";

        var chunk1 = new IndexedChunkDto
        {
            ChunkId = "chunk-001",
            IndexedArtifactId = artifactId,
            IngestionJobId = jobId
        };
        var chunk2 = new IndexedChunkDto
        {
            ChunkId = "chunk-002",
            IndexedArtifactId = artifactId,
            IngestionJobId = jobId
        };

        var manualDocument = ManualDocument.Create(
            documentId, "workshop-manual.pdf", "manuals", "workshop-manual.pdf",
            "manual", sourceContentHash: sourceContentHash);

        _chunkRepositoryMock
            .Setup(x => x.GetByArtifactIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { chunk1, chunk2 });
        _manualDocumentRepositoryMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(manualDocument);

        // Act
        var result = await _sut.BuildBackfillDocumentsAsync(artifactId, documentId);

        // Assert
        result.Should().HaveCount(2, "the backfill service must produce exactly one partial-update document per chunk");
        result.Select(d => d.Id).Should().BeEquivalentTo(
            TwoChunkIds,
            "each partial-update document must carry the chunk's ChunkId as the 'id' key field");
    }

    [Fact]
    public async Task BuildBackfillDocumentsAsync_PartialUpdateDocument_ContainsOnlyTheFourAnchorFields()
    {
        // Arrange
        var artifactId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var sourceContentHash = "sha256-def456";

        var chunk = new IndexedChunkDto
        {
            ChunkId = "chunk-solo",
            IndexedArtifactId = artifactId,
            IngestionJobId = jobId
        };

        var manualDocument = ManualDocument.Create(
            documentId, "manual.pdf", "manuals", "manual.pdf",
            "manual", sourceContentHash: sourceContentHash);

        _chunkRepositoryMock
            .Setup(x => x.GetByArtifactIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { chunk });
        _manualDocumentRepositoryMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(manualDocument);

        // Act
        var result = await _sut.BuildBackfillDocumentsAsync(artifactId, documentId);

        // Assert
        var updateDoc = result.Single();

        // --- structural: exactly four properties ---
        var props = typeof(ChunkAnchorUpdateDocument).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        props.Should().HaveCount(4, "a merge-patch document must carry only the four anchor fields");
        props.Select(p => p.Name).Should().BeEquivalentTo(AnchorFieldNames);

        // --- value correctness ---
        updateDoc.Id.Should().Be("chunk-solo");
        updateDoc.IndexedArtifactId.Should().Be(artifactId.ToString());
        updateDoc.IngestionJobId.Should().Be(jobId.ToString());
        updateDoc.SourceContentHash.Should().Be(sourceContentHash);

        // --- every ChunkIndexRecord non-anchor field must be absent ---
        // These are the known fields from the Azure AI Search index schema (ChunkIndexRecord)
        // that must NOT appear in a partial-update (merge-patch) document.
        // A merge-patch that includes contentVector would trigger a full re-index
        // (the vector field is non-mergeable), and including other fields like content
        // would overwrite their existing values with defaults — both are forbidden.
        var propertyNameSet = props.Select(p => p.Name).ToHashSet();

        var presentButForbidden = ForbiddenFields
            .Where(f => propertyNameSet.Contains(f))
            .ToArray();

        // The four anchor fields ARE present (verified above); the remaining 19 ChunkIndexRecord
        // fields MUST be absent — and structurally they ARE, because ChunkAnchorUpdateDocument only
        // declares the four. This assertion proves no accidental field was added.
        presentButForbidden.Should().BeEquivalentTo(
            AnchorFieldNames,
            "the only allowed fields are the four anchors — no other ChunkIndexRecord field should appear");

        // Double-check: the forbidden set minus the four anchors must have zero overlap with props
        var nonAnchorForbidden = ForbiddenFields.Except(AnchorFieldNames).ToHashSet();
        var overlap = propertyNameSet.Intersect(nonAnchorForbidden).ToArray();
        overlap.Should().BeEmpty(
            $"none of these ChunkIndexRecord fields may leak into the partial-update document: {string.Join(", ", nonAnchorForbidden)}");
    }

    [Fact]
    public async Task BuildBackfillDocumentsAsync_WhenSerialized_ProducesMergePatchJsonWithOnlyFourKeys()
    {
        // Arrange — non-null hash: the patch must carry ALL FOUR anchor keys.
        var artifactId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var sourceContentHash = "sha256-json-test";

        var chunk = new IndexedChunkDto
        {
            ChunkId = "json-chunk",
            IndexedArtifactId = artifactId,
            IngestionJobId = jobId
        };

        var manualDocument = ManualDocument.Create(
            documentId, "manual.pdf", "manuals", "manual.pdf",
            "manual", sourceContentHash: sourceContentHash);

        _chunkRepositoryMock
            .Setup(x => x.GetByArtifactIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { chunk });
        _manualDocumentRepositoryMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(manualDocument);

        // Act
        var result = await _sut.BuildBackfillDocumentsAsync(artifactId, documentId);

        // Assert — DEFAULT JsonSerializerOptions (the condition any operator harness uses).
        // The wire shape is a property of the TYPE ([JsonPropertyName] + [JsonIgnore(WhenWritingNull)]),
        // NOT supplied by the caller — so default serialization must produce the correct merge-patch.
        // This is the dispositive proof that the §7 null-clobber safeguard lives on the type.
        var updateDoc = result.Single();
        var json = JsonSerializer.Serialize(updateDoc);

        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        // Non-null hash -> all four anchor keys present (the full patch).
        var jsonKeys = root.EnumerateObject().Select(p => p.Name).ToHashSet();
        jsonKeys.Should().BeEquivalentTo(
            ExpectedAnchorJsonKeys,
            "with a non-null hash the serialized JSON must carry all four anchor keys under default options");

        root.GetProperty("id").GetString().Should().Be("json-chunk");
        root.GetProperty("indexedArtifactId").GetString().Should().Be(artifactId.ToString());
        root.GetProperty("ingestionJobId").GetString().Should().Be(jobId.ToString());
        root.GetProperty("sourceContentHash").GetString().Should().Be(sourceContentHash);
    }

    [Fact]
    public async Task BuildBackfillDocumentsAsync_WhenSourceContentHashIsNull_OmitsKeyUnderDefaultSerialization()
    {
        // Arrange — null hash (manual document missing): the patch must OMIT the
        // sourceContentHash key entirely so Azure AI Search mergeOrUpload leaves any
        // existing hash untouched, instead of treating an explicit null as "clear field".
        var artifactId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var chunk = new IndexedChunkDto
        {
            ChunkId = "null-hash-json",
            IndexedArtifactId = artifactId,
            IngestionJobId = Guid.NewGuid()
        };

        _chunkRepositoryMock
            .Setup(x => x.GetByArtifactIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { chunk });
        _manualDocumentRepositoryMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManualDocument?)null);

        // Act
        var result = await _sut.BuildBackfillDocumentsAsync(artifactId, documentId);

        // Assert — DEFAULT JsonSerializerOptions. The [JsonIgnore(WhenWritingNull)] attribute on
        // the type must cause the sourceContentHash key to be ABSENT (not emitted as null). This
        // is the real-world operator-harness condition and the exact defense against §7.
        var updateDoc = result.Single();
        updateDoc.SourceContentHash.Should().BeNull();
        var json = JsonSerializer.Serialize(updateDoc);

        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        var jsonKeys = root.EnumerateObject().Select(p => p.Name).ToHashSet();
        jsonKeys.Should().BeEquivalentTo(
            ExpectedAnchorJsonKeysWithoutHash,
            "a null sourceContentHash must be OMITTED under default serialization — the key must not appear, "
            + "because an explicit \"sourceContentHash\": null would clobber any existing value (the §7 defect)");

        // Dispositive: the key is NOT present, full stop.
        root.TryGetProperty("sourceContentHash", out _).Should().BeFalse(
            "sourceContentHash must not be emitted as a JSON key when null — WhenWritingNull omits it");

        // The other three anchors are still present.
        root.GetProperty("id").GetString().Should().Be("null-hash-json");
        root.GetProperty("indexedArtifactId").GetString().Should().Be(artifactId.ToString());
        root.GetProperty("ingestionJobId").GetString().Should().NotBeNull();
    }

    [Fact]
    public async Task BuildBackfillDocumentsAsync_WhenNoChunksForArtifact_ReturnsEmptyCollection()
    {
        // Arrange
        var artifactId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        _chunkRepositoryMock
            .Setup(x => x.GetByArtifactIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IndexedChunkDto>());

        // The document is still fetched — but with no chunks there's nothing to stamp.
        var manualDocument = ManualDocument.Create(
            documentId, "empty.pdf", "manuals", "empty.pdf",
            "manual", sourceContentHash: "sha256-empty");
        _manualDocumentRepositoryMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(manualDocument);

        // Act
        var result = await _sut.BuildBackfillDocumentsAsync(artifactId, documentId);

        // Assert
        result.Should().BeEmpty("no partial-update documents should be produced when there are no chunks");
    }

    [Fact]
    public async Task BuildBackfillDocumentsAsync_WhenManualDocumentNotFound_SetsSourceContentHashToNull()
    {
        // Arrange
        var artifactId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var chunk = new IndexedChunkDto
        {
            ChunkId = "orphan-chunk",
            IndexedArtifactId = artifactId,
            IngestionJobId = Guid.NewGuid()
        };

        _chunkRepositoryMock
            .Setup(x => x.GetByArtifactIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { chunk });
        _manualDocumentRepositoryMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManualDocument?)null);

        // Act
        var result = await _sut.BuildBackfillDocumentsAsync(artifactId, documentId);

        // Assert
        var updateDoc = result.Single();
        updateDoc.SourceContentHash.Should().BeNull(
            "when the ManualDocument is not found, sourceContentHash must be null " +
            "so the merge-patch omits the key entirely via JsonIgnore(WhenWritingNull) " +
            "rather than clobbering any existing hash on the chunk");
        updateDoc.Id.Should().Be("orphan-chunk");
        updateDoc.IndexedArtifactId.Should().Be(artifactId.ToString());
        updateDoc.IngestionJobId.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildBackfillDocumentsAsync_WhenSourceContentHashIsNull_SetsNullOnUpdateDocument()
    {
        // Arrange
        var artifactId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var chunk = new IndexedChunkDto
        {
            ChunkId = "null-hash-chunk",
            IndexedArtifactId = artifactId,
            IngestionJobId = Guid.NewGuid()
        };

        // ManualDocument.Create allows sourceContentHash: null (default)
        var manualDocument = ManualDocument.Create(
            documentId, "no-hash.pdf", "manuals", "no-hash.pdf", "manual");

        _chunkRepositoryMock
            .Setup(x => x.GetByArtifactIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { chunk });
        _manualDocumentRepositoryMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(manualDocument);

        // Act
        var result = await _sut.BuildBackfillDocumentsAsync(artifactId, documentId);

        // Assert
        var updateDoc = result.Single();
        updateDoc.SourceContentHash.Should().BeNull(
            "when the source document has no content hash, the partial-update should carry null");
    }

    [Fact]
    public async Task BuildBackfillDocumentsAsync_MultipleChunks_EachCarriesCorrectAnchorMapping()
    {
        // Arrange
        var artifactId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var sourceContentHash = "sha256-multi";

        var chunks = Enumerable.Range(0, 5).Select(i => new IndexedChunkDto
        {
            ChunkId = $"chunk-{i:D3}",
            IndexedArtifactId = artifactId,
            IngestionJobId = jobId,
            PageNumber = i + 1,     // should NOT appear in the partial-update doc
            ChunkIndex = i,          // should NOT appear in the partial-update doc
            SourceFileName = $"page-{i + 1}.txt"  // should NOT appear in the partial-update doc
        }).ToList();

        var manualDocument = ManualDocument.Create(
            documentId, "multi-page.pdf", "manuals", "multi-page.pdf",
            "manual", sourceContentHash: sourceContentHash);

        _chunkRepositoryMock
            .Setup(x => x.GetByArtifactIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);
        _manualDocumentRepositoryMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(manualDocument);

        // Act
        var result = await _sut.BuildBackfillDocumentsAsync(artifactId, documentId);

        // Assert
        result.Should().HaveCount(5);

        for (var i = 0; i < 5; i++)
        {
            var doc = result[i];
            doc.Id.Should().Be($"chunk-{i:D3}");
            doc.IndexedArtifactId.Should().Be(artifactId.ToString());
            doc.IngestionJobId.Should().Be(jobId.ToString());
            doc.SourceContentHash.Should().Be(sourceContentHash);

            // Each document must only have 4 properties — verify via JSON serialization
            // under DEFAULT options. The type's [JsonPropertyName] attributes drive the
            // wire names, so default serialization produces the index-schema keys.
            var json = JsonSerializer.Serialize(doc);
            using var parsed = JsonDocument.Parse(json);
            parsed.RootElement.EnumerateObject().Count().Should().Be(4,
                $"chunk {i} partial-update document must have exactly 4 JSON keys");
        }
    }

    [Fact]
    public async Task BuildBackfillDocumentsAsync_DoesNotOpenLiveAzureConnection()
    {
        // Arrange
        var artifactId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var chunk = new IndexedChunkDto
        {
            ChunkId = "no-azure-chunk",
            IndexedArtifactId = artifactId,
            IngestionJobId = Guid.NewGuid()
        };

        _chunkRepositoryMock
            .Setup(x => x.GetByArtifactIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { chunk });
        _manualDocumentRepositoryMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManualDocument?)null);

        // Act
        var result = await _sut.BuildBackfillDocumentsAsync(artifactId, documentId);

        // Assert: the service should NOT call any indexing/upload method.
        // We verify this by checking that the only repos touched are the two we mocked.
        // The service has no IChunkIndexingService dependency — construction only.
        result.Should().HaveCount(1);
        _chunkRepositoryMock.Verify(
            x => x.GetByArtifactIdAsync(artifactId, It.IsAny<CancellationToken>()),
            Times.Once);
        _manualDocumentRepositoryMock.Verify(
            x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()),
            Times.Once);

        // No other verifications needed — the service signature exposes only construction,
        // not execution against Azure Search.
    }

    [Fact]
    public async Task BuildBackfillDocumentsAsync_ChunkIdIsLowerCase_IsPreservedAsGiven()
    {
        // Verifies that the service does not mutate the ChunkId value — the caller owns
        // it and the Azure AI Search index key must match exactly.
        // Arrange
        var artifactId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var chunk = new IndexedChunkDto
        {
            ChunkId = "CHUNK-MixedCase-ID",
            IndexedArtifactId = artifactId,
            IngestionJobId = Guid.NewGuid()
        };

        _chunkRepositoryMock
            .Setup(x => x.GetByArtifactIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { chunk });
        _manualDocumentRepositoryMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManualDocument?)null);

        // Act
        var result = await _sut.BuildBackfillDocumentsAsync(artifactId, documentId);

        // Assert
        result.Single().Id.Should().Be("CHUNK-MixedCase-ID",
            "ChunkId must be preserved exactly as stored — no normalization");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BuildBackfillDocumentsAsync_WhenSourceContentHashIsNullOrWhitespace_SetsNull(
        string? hashValue)
    {
        // Arrange
        var artifactId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var chunk = new IndexedChunkDto
        {
            ChunkId = "null-hash-chunk",
            IndexedArtifactId = artifactId,
            IngestionJobId = Guid.NewGuid()
        };

        // Construct a ManualDocument with the specific hash value via Rehydrate
        var manualDocument = ManualDocument.Rehydrate(
            documentId, "manual.pdf", "manuals", "manual.pdf", null,
            hashValue, "manual", null, null, null,
            DateTimeOffset.UtcNow, null, null,
            MotorcycleRAG.Domain.Enums.ManualDocumentStatus.Pending,
            null, null, null);

        _chunkRepositoryMock
            .Setup(x => x.GetByArtifactIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { chunk });
        _manualDocumentRepositoryMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(manualDocument);

        // Act
        var result = await _sut.BuildBackfillDocumentsAsync(artifactId, documentId);

        // Assert
        var updateDoc = result.Single();

        if (string.IsNullOrWhiteSpace(hashValue))
        {
            updateDoc.SourceContentHash.Should().BeNull(
                "whitespace-only or empty hash must be treated as null to avoid clobbering");
        }
        else
        {
            updateDoc.SourceContentHash.Should().Be(hashValue);
        }
    }

    [Fact]
    public async Task BuildBackfillDocumentsAsync_PartialUpdateUsesChunkIdNotIndexedArtifactId_AsKey()
    {
        // The Azure AI Search document key is 'id' (ChunkIndexRecord.Id), NOT
        // indexedArtifactId. This test verifies the service maps ChunkId → Id correctly
        // and does not accidentally use the artifact ID as the search document key.
        // Arrange
        var artifactId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var chunkId = "the-chunk-key";

        var chunk = new IndexedChunkDto
        {
            ChunkId = chunkId,
            IndexedArtifactId = artifactId,
            IngestionJobId = Guid.NewGuid()
        };

        _chunkRepositoryMock
            .Setup(x => x.GetByArtifactIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { chunk });
        _manualDocumentRepositoryMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManualDocument?)null);

        // Act
        var result = await _sut.BuildBackfillDocumentsAsync(artifactId, documentId);

        // Assert
        var updateDoc = result.Single();
        updateDoc.Id.Should().Be(chunkId,
            "the search document 'id' key must be the ChunkId, not the artifact ID");
        updateDoc.IndexedArtifactId.Should().Be(artifactId.ToString(),
            "indexedArtifactId is a separate anchor field, not the document key");
    }

    [Fact]
    public async Task BuildBackfillDocumentsAsync_BothRepositoryCallsPassCancellationToken()
    {
        // Verifies that the cancellation token is forwarded to both repository calls.
        // Arrange
        var artifactId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        CancellationToken? capturedChunkToken = null;
        CancellationToken? capturedManualToken = null;

        _chunkRepositoryMock
            .Setup(x => x.GetByArtifactIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((_, ct) => capturedChunkToken = ct)
            .ReturnsAsync(Array.Empty<IndexedChunkDto>());
        _manualDocumentRepositoryMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((_, ct) => capturedManualToken = ct)
            .ReturnsAsync((ManualDocument?)null);

        using var cts = new CancellationTokenSource();

        // Act
        await _sut.BuildBackfillDocumentsAsync(artifactId, documentId, cts.Token);

        // Assert
        capturedChunkToken.Should().NotBeNull();
        capturedChunkToken!.Value.Should().Be(cts.Token,
            "the cancellation token must be forwarded to IIndexedChunkRepository");
        capturedManualToken.Should().NotBeNull();
        capturedManualToken!.Value.Should().Be(cts.Token,
            "the cancellation token must be forwarded to IManualDocumentRepository");
    }
}
