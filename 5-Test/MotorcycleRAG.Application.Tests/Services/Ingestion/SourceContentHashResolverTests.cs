using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

// --- T2 (plan: 2026-08-02-anchor-id-debt-cleanup, D2) ---
// SourceContentHashResolver is the shared helper extracted from the two byte-identical private
// copies that lived in ProcessorArtifactService and ChunkReprocessService. Its contract is the
// §7 null-vs-empty defense: it returns null when there is no owning ManualDocument (either because
// the job has no ManualDocumentId, or because the repository lookup returns no row), and otherwise
// returns the document's SourceContentHash -- which may itself be null, but is NEVER coerced to
// string.Empty. An empty string would serialize as a real key and Azure AI Search mergeOrUpload
// would treat it as "clear this field", silently wiping a real hash already indexed. These tests
// pin all four branches of that contract directly against the helper, independent of either
// consumer; the two consumer test files (ProcessorArtifactServiceTests / ChunkReprocessServiceTests)
// remain as the regression net with zero assertion changes.

public class SourceContentHashResolverTests
{
    [Fact]
    public async Task ResolveAsync_WhenJobHasNoManualDocumentId_ReturnsNullWithoutRepositoryCall()
    {
        var manualDocumentRepoMock = new Mock<IManualDocumentRepository>();
        // IngestionJob.Create always leaves ManualDocumentId null (the freshly-created-job shape).
        var job = IngestionJob.Create(IngestionJobType.PDFManual, "uploads/no-manual", createdBySubject: null);

        var result = await SourceContentHashResolver.ResolveAsync(manualDocumentRepoMock.Object, job, CancellationToken.None);

        result.Should().BeNull(
            "a job with no ManualDocumentId has no owning document to resolve a hash from -- null is the " +
            "value the T7 JsonIgnore(WhenWritingNull) guard then omits from the merge payload");
        manualDocumentRepoMock.Verify(
            x => x.GetDocumentByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "when ManualDocumentId is null there is nothing to look up -- the repository must not be called");
    }

    [Fact]
    public async Task ResolveAsync_WhenManualDocumentNotFound_ReturnsNull()
    {
        var documentId = Guid.NewGuid();
        var manualDocumentRepoMock = new Mock<IManualDocumentRepository>();
        manualDocumentRepoMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManualDocument?)null);

        var job = RehydrateJobWithManualDocumentId(documentId);

        var result = await SourceContentHashResolver.ResolveAsync(manualDocumentRepoMock.Object, job, CancellationToken.None);

        result.Should().BeNull(
            "a missing ManualDocument row must resolve to a null hash -- never string.Empty, which " +
            "mergeOrUpload would still treat as a real value that clobbers any hash already indexed");
    }

    [Fact]
    public async Task ResolveAsync_WhenManualDocumentHasHash_ReturnsThatHash()
    {
        var documentId = Guid.NewGuid();
        var testHash = "sha256-realhash-fromdocument";
        var document = ManualDocument.Create(
            documentId: documentId,
            sourceFileName: "manual.pdf",
            canonicalBlobContainer: "manuals",
            canonicalBlobPath: $"manuals/{documentId}/manual.pdf",
            documentType: "manual-pdf",
            sourceContentHash: testHash);

        var manualDocumentRepoMock = new Mock<IManualDocumentRepository>();
        manualDocumentRepoMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var job = RehydrateJobWithManualDocumentId(documentId);

        var result = await SourceContentHashResolver.ResolveAsync(manualDocumentRepoMock.Object, job, CancellationToken.None);

        result.Should().Be(testHash,
            "the owning ManualDocument.SourceContentHash must be passed through unchanged -- this is what " +
            "anchors indexed chunks back to their source document version");
        manualDocumentRepoMock.Verify(
            x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()),
            Times.Once,
            "when ManualDocumentId is set the repository must actually be consulted");
    }

    [Fact]
    public async Task ResolveAsync_WhenManualDocumentHasNullHash_PropagatesNullNeverEmpty()
    {
        var documentId = Guid.NewGuid();
        // A persisted ManualDocument may legitimately carry a null SourceContentHash (e.g. the hash
        // has not been computed yet). The helper must propagate that null verbatim -- coercing it to
        // string.Empty would re-open the §7 null-clobber defect.
        var document = ManualDocument.Create(
            documentId: documentId,
            sourceFileName: "manual.pdf",
            canonicalBlobContainer: "manuals",
            canonicalBlobPath: $"manuals/{documentId}/manual.pdf",
            documentType: "manual-pdf",
            sourceContentHash: null);

        var manualDocumentRepoMock = new Mock<IManualDocumentRepository>();
        manualDocumentRepoMock
            .Setup(x => x.GetDocumentByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var job = RehydrateJobWithManualDocumentId(documentId);

        var result = await SourceContentHashResolver.ResolveAsync(manualDocumentRepoMock.Object, job, CancellationToken.None);

        result.Should().BeNull(
            "a document whose SourceContentHash is itself null must propagate that null -- the helper " +
            "must never substitute string.Empty");
        result.Should().NotBe(string.Empty,
            "string.Empty is NOT an acceptable substitute for null -- mergeOrUpload still writes an empty " +
            "string as a real field value, clobbering any real hash already indexed for this chunk");
    }

    // IngestionJob.Create always leaves ManualDocumentId null; only Rehydrate can set it, which is
    // exactly the persisted-row shape that carries a document link.
    private static IngestionJob RehydrateJobWithManualDocumentId(Guid documentId) => IngestionJob.Rehydrate(
        id: 0,
        ingestionJobId: Guid.NewGuid(),
        createdAtUtc: DateTimeOffset.UtcNow,
        startedAtUtc: null,
        completedAtUtc: null,
        createdBySubject: null,
        status: IngestionJobStatus.Queued,
        failureReason: null,
        errorsJson: null,
        errorMessage: null,
        inputType: IngestionJobType.PDFManual,
        inputRef: "uploads/linked-manual",
        sourceFileName: null,
        computeProvider: "MicrosoftFabric",
        docIngestionRunId: null,
        manualDocumentId: documentId,
        totalPages: null,
        pagesCapturedViewableCount: null,
        pagesWithSearchableTextCount: null,
        pagesWithOcrTextCount: null,
        pagesWithNativeTextCount: null,
        missingPagesJson: null,
        metricsJson: null,
        expectedChunkCount: null,
        indexedChunkCount: null,
        currentStage: null,
        stageSetAtUtc: null,
        metadataJson: null);
}
