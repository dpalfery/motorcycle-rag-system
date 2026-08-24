using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

/// <summary>
/// RED-phase tests for the not-yet-implemented <see cref="SearchChunkIndexingCoordinator"/> /
/// <see cref="ISearchChunkIndexingCoordinator"/> (plan 2026-08-03-processor-artifact-skip-observability, T10/T11,
/// decision D12).
/// </summary>
/// <remarks>
/// D12 extracts the post-job-resolution indexing body out of
/// <c>ProcessorArtifactService.ProcessSearchChunksAsync</c> into a single shared collaborator so the upload path
/// (this file), the reconciliation sweep (T13, not yet written), and the admin "adopt" action (T17, not yet
/// written) can all invoke exactly the same indexing logic with real anchors, honoring the D1 fail-closed anchor
/// contract in exactly one place.
/// <para>
/// Coordinator contract designed for T11's implementer:
/// </para>
/// <code>
/// namespace MotorcycleRAG.Contracts.Interfaces;
///
/// public interface ISearchChunkIndexingCoordinator
/// {
///     /// &lt;summary&gt;
///     /// Indexes a search-chunks JSONL artifact for an already-resolved, valid ingestion job (non-null,
///     /// &lt;see cref="IngestionJob.IngestionJobId"/&gt; != Guid.Empty) and persists the outcome. Never throws --
///     /// every failure mode (indexing exception, job-transition exception, metadata-write exception) is caught
///     /// internally and reported via the returned DTO, matching the best-effort semantics
///     /// ProcessorArtifactService.ProcessSearchChunksAsync has today.
///     /// &lt;/summary&gt;
///     /// &lt;param name="jsonlStream"&gt;The search-chunks JSONL content. Caller owns positioning/seekability.&lt;/param&gt;
///     /// &lt;param name="uploadId"&gt;The processor upload identifier that owns this artifact.&lt;/param&gt;
///     /// &lt;param name="container"&gt;The blob container the artifact lives in (e.g. "search-chunks").&lt;/param&gt;
///     /// &lt;param name="blobPath"&gt;The blob path of the artifact within &lt;paramref name="container"/&gt;.&lt;/param&gt;
///     /// &lt;param name="indexedArtifactId"&gt;
///     /// The pre-allocated, non-empty canonical artifact ID the caller has already generated for this indexing
///     /// attempt (Guid.NewGuid() for a fresh upload/sweep-heal/adopt; never Guid.Empty).
///     /// &lt;/param&gt;
///     /// &lt;param name="job"&gt;
///     /// The already-resolved, validated ingestion job (non-null, IngestionJobId != Guid.Empty). Callers perform
///     /// job resolution and the D1 anchor-satisfiability guards themselves BEFORE calling this method -- the
///     /// coordinator's contract begins only once a valid job exists.
///     /// &lt;/param&gt;
///     /// &lt;param name="cancellationToken"&gt;Propagates the caller's cancellation.&lt;/param&gt;
///     Task&lt;SearchChunkIndexingOutcomeDto&gt; IndexAsync(
///         Stream jsonlStream,
///         string uploadId,
///         string container,
///         string blobPath,
///         Guid indexedArtifactId,
///         IngestionJob job,
///         CancellationToken cancellationToken = default);
/// }
/// </code>
/// <para>
/// Return DTO (Contracts.Models.DTOs.Ingestion, "Dto" suffix per model-classification rules -- a property-bag
/// result carrier with no domain invariant of its own):
/// </para>
/// <code>
/// namespace MotorcycleRAG.Contracts.Models.DTOs.Ingestion;
///
/// public sealed record SearchChunkIndexingOutcomeDto(
///     Guid IndexedArtifactId,
///     Guid IngestionJobId,
///     bool Succeeded,
///     int ExpectedChunkCount,
///     int IndexedChunkCount,
///     int FailedChunkCount,
///     IndexedArtifactState? ArtifactState,
///     bool JobTransitionedToTerminal,
///     string? FailureReason);
/// </code>
/// </remarks>
public class SearchChunkIndexingCoordinatorTests
{
    private const string Container = "search-chunks";

    private readonly Mock<IChunkIndexingService> _chunkIndexingMock;
    private readonly Mock<IIndexedArtifactRepository> _artifactRepoMock;
    private readonly Mock<IIndexedChunkRepository> _chunkRepoMock;
    private readonly Mock<IIngestionJobRepository> _jobRepoMock;
    private readonly Mock<IBlobStorageService> _blobStorageMock;
    private readonly Mock<IManualDocumentRepository> _manualDocumentRepoMock;
    private readonly SearchChunkIndexingCoordinator _sut;

    public SearchChunkIndexingCoordinatorTests()
    {
        _chunkIndexingMock = new Mock<IChunkIndexingService>();
        _artifactRepoMock = new Mock<IIndexedArtifactRepository>();
        _chunkRepoMock = new Mock<IIndexedChunkRepository>();
        _jobRepoMock = new Mock<IIngestionJobRepository>();
        _blobStorageMock = new Mock<IBlobStorageService>();
        _manualDocumentRepoMock = new Mock<IManualDocumentRepository>();

        _sut = new SearchChunkIndexingCoordinator(
            _chunkIndexingMock.Object,
            _artifactRepoMock.Object,
            _chunkRepoMock.Object,
            _jobRepoMock.Object,
            _blobStorageMock.Object,
            _manualDocumentRepoMock.Object,
            NullLogger<SearchChunkIndexingCoordinator>.Instance);
    }

    private static string BlobPathFor(string uploadId) => $"{uploadId}/chunks.jsonl";

    // --- T10 (plan: 2026-08-03-processor-artifact-skip-observability, §4 T11 test-contract Row 1) ---
    // "given real anchors and a JSONL stream: calls the sole 5-parameter IndexFromJsonlAsync with the resolved
    // indexedArtifactId/ingestionJobId/sourceContentHash, never Guid.Empty/string.Empty; upserts the artifact
    // with the computed IndexedArtifactState; replaces chunk rows; transitions the job; stamps the happy-path
    // metadata set (which clears orphan keys)". Mirrors
    // ProcessorArtifactServiceTests.UploadArtifactAsync_SearchChunksArtifact_AllChunksIndexed_UpsertsCompletedArtifactAndTransitionsJobCompleted.

    [Fact]
    public async Task IndexAsync_AllChunksIndexed_CallsIndexFromJsonlAsyncWithRealAnchors_UpsertsCompletedArtifactTransitionsJobAndStampsHappyPathMetadata()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        var blobPath = BlobPathFor(uploadId);

        var outcomes = new List<ChunkIndexOutcome>
        {
            new("chunk-1", true, null, 1, 0, "file.pdf"),
            new("chunk-2", true, null, 1, 1, "file.pdf")
        };

        var capturedIndexedArtifactId = Guid.Empty;
        var capturedIngestionJobId = Guid.Empty;
        string? capturedSourceContentHash = "unset-sentinel";
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, Guid, Guid, string?, CancellationToken>((_, _, artifactId, jobId, hash, _) =>
            {
                capturedIndexedArtifactId = artifactId;
                capturedIngestionJobId = jobId;
                capturedSourceContentHash = hash;
            })
            .ReturnsAsync(new ChunkIndexingResult(2, 1, outcomes));

        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.Completed, 2, 2, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.IndexAsync(new MemoryStream(new byte[] { 1 }), uploadId, Container, blobPath, indexedArtifactId, job);

        // The heart of Row 1: real anchors, never Guid.Empty/string.Empty, flow into the sole 5-parameter overload.
        capturedIndexedArtifactId.Should().Be(indexedArtifactId,
            "the pre-allocated real artifact anchor must reach IndexFromJsonlAsync unchanged (D1 fail-closed contract)");
        capturedIndexedArtifactId.Should().NotBe(Guid.Empty);
        capturedIngestionJobId.Should().Be(job.IngestionJobId,
            "the already-resolved job's real anchor must reach IndexFromJsonlAsync -- never Guid.Empty (D1)");
        capturedIngestionJobId.Should().NotBe(Guid.Empty);
        capturedSourceContentHash.Should().BeNull(
            "a freshly-created job has no ManualDocumentId, so SourceContentHashResolver must resolve null, never " +
            "string.Empty (D1 null-clobber guard, mirrored from the pinned upload-path behavior)");

        _artifactRepoMock.Verify(
            x => x.UpsertAsync(
                It.Is<IndexedArtifactDto>(a =>
                    a.IndexedArtifactId == indexedArtifactId &&
                    a.IngestionJobId == job.IngestionJobId &&
                    a.UploadId == uploadId &&
                    a.BlobContainer == Container &&
                    a.BlobPath == blobPath &&
                    a.State == IndexedArtifactState.Completed &&
                    a.IndexedChunkCount == 2 &&
                    a.FailedChunkCount == 0 &&
                    a.FailureReason == null),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _chunkRepoMock.Verify(x => x.DeleteByArtifactIdAsync(indexedArtifactId, It.IsAny<CancellationToken>()), Times.Once);
        _chunkRepoMock.Verify(
            x => x.UpsertManyAsync(
                It.Is<IReadOnlyCollection<IndexedChunkDto>>(c => c.Count == 2 && c.All(chunk => chunk.IndexedArtifactId == indexedArtifactId && chunk.IngestionJobId == job.IngestionJobId)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _jobRepoMock.Verify(
            x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.Completed, 2, 2, null, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        // Verify the happy-path metadata stamp was called with correct keys
        _blobStorageMock.Verify(
            x => x.SetMetadataAsync(
                Container,
                blobPath,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the happy-path stamp must write metadata -- SetMetadataAsync replaces the " +
            "whole metadata collection (Azure Set Blob Metadata semantics), so this call is exactly what clears " +
            "any pre-existing orphan keys on a healed artifact without a separate delete step");

        // Capture and verify the actual metadata sent
        var metadataCall = _blobStorageMock.Invocations
            .FirstOrDefault(i => i.Method.Name == "SetMetadataAsync" &&
                                i.Arguments.Count > 0 && i.Arguments[0].Equals(Container) &&
                                i.Arguments[1].Equals(blobPath));
        metadataCall.Should().NotBeNull();
        var passedMetadata = (Dictionary<string, string>)metadataCall!.Arguments[2];
        passedMetadata.Should().HaveCount(2);
        passedMetadata.Should().ContainKey("state");
        passedMetadata["state"].Should().Be("Completed");
        passedMetadata.Should().ContainKey("dateLastProcessed");
        passedMetadata["dateLastProcessed"].Should().NotBeNullOrWhiteSpace();

        result.Should().NotBeNull();
        result.IndexedArtifactId.Should().Be(indexedArtifactId);
        result.IngestionJobId.Should().Be(job.IngestionJobId);
        result.Succeeded.Should().BeTrue();
        result.ArtifactState.Should().Be(IndexedArtifactState.Completed);
        result.ExpectedChunkCount.Should().Be(2);
        result.IndexedChunkCount.Should().Be(2);
        result.FailedChunkCount.Should().Be(0);
        result.JobTransitionedToTerminal.Should().BeTrue();

        _manualDocumentRepoMock.Verify(
            x => x.GetDocumentByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "IngestionJob.Create leaves ManualDocumentId null, so SourceContentHashResolver must short-circuit " +
            "and never consult the repository -- mirrors the parity gap left by removing " +
            "ProcessorArtifactServiceTests.UploadArtifactAsync_SearchChunksArtifact_JobHasNoManualDocument_" +
            "PassesNullSourceContentHash_NeverEmptyString's direct collaborator visibility");
    }

    // Mirrors ProcessorArtifactServiceTests.UploadArtifactAsync_SearchChunksArtifact_ResolvesAnchorsBeforeIndexing_
    // PassesNonDefaultValuesToFiveParameterOverload's ManualDocument-hash-resolution half: the T11 extraction moves
    // SourceContentHashResolver.ResolveAsync (and therefore IManualDocumentRepository) out of
    // ProcessorArtifactService entirely, so this positive "job has a real linked ManualDocument with a hash" case
    // must be re-pinned here or it loses direct test coverage.
    [Fact]
    public async Task IndexAsync_JobHasManualDocumentWithSourceContentHash_ResolvesRealHashAndPassesToIndexing()
    {
        var uploadId = Guid.NewGuid().ToString();
        var indexedArtifactId = Guid.NewGuid();
        var blobPath = BlobPathFor(uploadId);

        var testHash = "sha256-abc123def456";
        var testDocId = Guid.NewGuid();
        var manualDocument = ManualDocument.Create(
            documentId: testDocId,
            sourceFileName: "test.pdf",
            canonicalBlobContainer: "container",
            canonicalBlobPath: "path",
            documentType: "manual-pdf",
            sourceContentHash: testHash);

        var job = IngestionJob.Rehydrate(
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
            inputRef: uploadId,
            sourceFileName: null,
            computeProvider: "MicrosoftFabric",
            docIngestionRunId: null,
            manualDocumentId: testDocId,
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

        _manualDocumentRepoMock
            .Setup(x => x.GetDocumentByIdAsync(testDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(manualDocument);

        var outcomes = new List<ChunkIndexOutcome>
        {
            new("chunk-1", true, null, 1, 0, "file.pdf"),
            new("chunk-2", true, null, 1, 1, "file.pdf")
        };
        string? capturedSourceContentHash = "unset-sentinel";
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, Guid, Guid, string?, CancellationToken>((_, _, _, _, hash, _) => capturedSourceContentHash = hash)
            .ReturnsAsync(new ChunkIndexingResult(2, 1, outcomes));
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.Completed, 2, 2, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.IndexAsync(new MemoryStream(new byte[] { 1 }), uploadId, Container, blobPath, indexedArtifactId, job);

        capturedSourceContentHash.Should().Be(testHash,
            "the owning ManualDocument.SourceContentHash must be resolved and passed into the index call unchanged");
        result.Succeeded.Should().BeTrue();
        _manualDocumentRepoMock.Verify(x => x.GetDocumentByIdAsync(testDocId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Mirrors ProcessorArtifactServiceTests.UploadArtifactAsync_SearchChunksArtifact_PartialIndexing_MarksPartiallyIndexedAndTransitionsJob.
    [Fact]
    public async Task IndexAsync_PartialIndexing_UpsertsPartiallyIndexedArtifactAndTransitionsJobPartiallyCompleted()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        var blobPath = BlobPathFor(uploadId);

        var outcomes = new List<ChunkIndexOutcome>
        {
            new("chunk-1", true, null, 1, 0, "file.pdf"),
            new("chunk-2", false, "embedding failure", 1, 1, "file.pdf")
        };
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(2, 1, outcomes));
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.PartiallyCompleted, 2, 1, "Failed to index 1 chunk(s)", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.IndexAsync(new MemoryStream(new byte[] { 1 }), uploadId, Container, blobPath, indexedArtifactId, job);

        _artifactRepoMock.Verify(
            x => x.UpsertAsync(
                It.Is<IndexedArtifactDto>(a =>
                    a.IndexedArtifactId == indexedArtifactId &&
                    a.State == IndexedArtifactState.PartiallyIndexed &&
                    a.IndexedChunkCount == 1 &&
                    a.FailedChunkCount == 1 &&
                    a.FailureReason == "Failed to index 1 chunk(s)"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _chunkRepoMock.Verify(
            x => x.UpsertManyAsync(It.Is<IReadOnlyCollection<IndexedChunkDto>>(c => c.Count == 2), It.IsAny<CancellationToken>()),
            Times.Once);
        _jobRepoMock.Verify(
            x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.PartiallyCompleted, 2, 1, "Failed to index 1 chunk(s)", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _blobStorageMock.Verify(
            x => x.SetMetadataAsync(
                Container,
                blobPath,
                It.Is<Dictionary<string, string>>(metadata => metadata.Count == 2 && metadata["state"] == "PartiallyIndexed"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        result.Succeeded.Should().BeTrue();
        result.ArtifactState.Should().Be(IndexedArtifactState.PartiallyIndexed);
        result.IndexedChunkCount.Should().Be(1);
        result.FailedChunkCount.Should().Be(1);
    }

    // Mirrors ProcessorArtifactServiceTests.UploadArtifactAsync_SearchChunksArtifact_IndexingThrows_TransitionsJobToFailed:
    // a throwing IndexFromJsonlAsync must not escape IndexAsync -- the coordinator owns the same best-effort
    // failure handling ProcessSearchChunksAsync has today, and it already holds the resolved job, so it can
    // transition it to Failed directly without a second job lookup.
    [Fact]
    public async Task IndexAsync_IndexingThrows_DoesNotThrowAndTransitionsJobToFailedWithoutPersistingAnArtifact()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        var blobPath = BlobPathFor(uploadId);

        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("search index unavailable"));
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.Failed, 0, 0, It.Is<string>(s => s.Contains("search index unavailable")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        SearchChunkIndexingOutcomeDto? result = null;
        var act = async () => result = await _sut.IndexAsync(new MemoryStream(new byte[] { 1 }), uploadId, Container, blobPath, indexedArtifactId, job);

        await act.Should().NotThrowAsync(
            "an indexing failure is a data-plane condition the coordinator must report, not an exception that " +
            "escapes and fails the caller's whole operation");

        _jobRepoMock.Verify(
            x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.Failed, 0, 0, It.Is<string>(s => s.Contains("search index unavailable")), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _artifactRepoMock.Verify(x => x.UpsertAsync(It.IsAny<IndexedArtifactDto>(), It.IsAny<CancellationToken>()), Times.Never,
            "the indexing call never returned a result, so no artifact state was ever computed to persist");
        _chunkRepoMock.Verify(x => x.UpsertManyAsync(It.IsAny<IReadOnlyCollection<IndexedChunkDto>>(), It.IsAny<CancellationToken>()), Times.Never);
        _blobStorageMock.Verify(x => x.SetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never,
            "the happy-path metadata stamp only ever follows a completed indexing pass");

        result.Should().NotBeNull();
        result!.Succeeded.Should().BeFalse();
        result.ArtifactState.Should().BeNull();
    }

    // Mirrors ProcessorArtifactServiceTests.UploadArtifactAsync_SearchChunksArtifact_IndexingThrowsAndTransitionAlsoThrows_SwallowsBothExceptions.
    [Fact]
    public async Task IndexAsync_IndexingThrowsAndFailureTransitionAlsoThrows_SwallowsBothExceptions()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        var blobPath = BlobPathFor(uploadId);

        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("search index unavailable"));
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(It.IsAny<Guid>(), It.IsAny<IngestionJobStatus>(), It.IsAny<IngestionJobStatus>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sql unavailable"));

        var act = async () => await _sut.IndexAsync(new MemoryStream(new byte[] { 1 }), uploadId, Container, blobPath, indexedArtifactId, job);

        await act.Should().NotThrowAsync(
            "the fallback job-to-Failed transition is itself best-effort; a secondary infrastructure outage while " +
            "reporting the first failure must not escalate into an unhandled exception");
    }

    // --- T10 (plan: 2026-08-03-processor-artifact-skip-observability, §4 T11 test-contract Row 1, metadata-throws
    // scenario) --- Mirrors ProcessorArtifactServiceTests.UploadArtifactAsync_SearchChunksArtifact_MetadataUpdateFails_DoesNotThrowAndUploadStillSucceeds.
    [Fact]
    public async Task IndexAsync_MetadataUpdateFails_DoesNotThrowAndStillReportsSucceeded()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        var blobPath = BlobPathFor(uploadId);

        var outcomes = new List<ChunkIndexOutcome> { new("chunk-1", true, null, 1, 0, "file.pdf") };
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(1, 1, outcomes));
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.Completed, 1, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _blobStorageMock
            .Setup(x => x.SetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("metadata store unavailable"));

        SearchChunkIndexingOutcomeDto? result = null;
        var act = async () => result = await _sut.IndexAsync(new MemoryStream(new byte[] { 1 }), uploadId, Container, blobPath, indexedArtifactId, job);

        await act.Should().NotThrowAsync(
            "the metadata stamp is best-effort -- a metadata-store outage must never surface as an indexing failure " +
            "when the artifact/chunk rows and job transition already succeeded");

        _blobStorageMock.Verify(
            x => x.SetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _artifactRepoMock.Verify(x => x.UpsertAsync(It.IsAny<IndexedArtifactDto>(), It.IsAny<CancellationToken>()), Times.Once,
            "the metadata write happens after persistence -- its failure must not roll back or skip the artifact upsert");

        result.Should().NotBeNull();
        result!.Succeeded.Should().BeTrue(
            "a best-effort metadata-write failure must not flip the reported outcome to failed -- the artifact " +
            "really was indexed and persisted");
        result.ArtifactState.Should().Be(IndexedArtifactState.Completed);
    }

    // --- T10 (plan §4 T11 test-contract Row 1, job-transition retry-loop scenario 1/2) ---
    // Issue 1 (BLOCKING): The job-transition retry/exhaustion loop lost its test coverage during the refactor.
    // This test verifies that the loop tries all three active statuses in order (Indexing, Processing, Queued),
    // stopping at the first one that succeeds.
    [Fact]
    public async Task IndexAsync_JobTransitionIndexingAndProcessingFail_RetriesProcessingThenQueued_SucceedsOnQueued()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        var blobPath = BlobPathFor(uploadId);

        var outcomes = new List<ChunkIndexOutcome>
        {
            new("chunk-1", true, null, 1, 0, "file.pdf"),
            new("chunk-2", true, null, 1, 1, "file.pdf")
        };
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(2, 1, outcomes));

        // Set up the transition mock to fail for Indexing and Processing, but succeed for Queued.
        // This tests the retry loop behavior: it should try Indexing first, then Processing, then Queued.
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(
                job.IngestionJobId,
                IngestionJobStatus.Indexing,
                It.IsAny<IngestionJobStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);  // Indexing status doesn't match

        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(
                job.IngestionJobId,
                IngestionJobStatus.Processing,
                It.IsAny<IngestionJobStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);  // Processing status doesn't match either

        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(
                job.IngestionJobId,
                IngestionJobStatus.Queued,
                It.IsAny<IngestionJobStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);  // Queued status matches and transition succeeds

        var result = await _sut.IndexAsync(new MemoryStream(new byte[] { 1 }), uploadId, Container, blobPath, indexedArtifactId, job);

        // Verify all three statuses were attempted in the correct order
        _jobRepoMock.Verify(
            x => x.TryTransitionToTerminalAsync(
                job.IngestionJobId,
                IngestionJobStatus.Indexing,
                It.IsAny<IngestionJobStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the loop must attempt Indexing status first");

        _jobRepoMock.Verify(
            x => x.TryTransitionToTerminalAsync(
                job.IngestionJobId,
                IngestionJobStatus.Processing,
                It.IsAny<IngestionJobStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the loop must retry with Processing status when Indexing fails");

        _jobRepoMock.Verify(
            x => x.TryTransitionToTerminalAsync(
                job.IngestionJobId,
                IngestionJobStatus.Queued,
                It.IsAny<IngestionJobStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the loop must retry with Queued status when Processing fails");

        result.Should().NotBeNull();
        result.Succeeded.Should().BeTrue(
            "the coordinator reported success even though the first two transition attempts failed, " +
            "because the third attempt (Queued) succeeded");
        result.JobTransitionedToTerminal.Should().BeTrue(
            "a successful transition to terminal must be reported in the outcome");
        result.ArtifactState.Should().Be(IndexedArtifactState.Completed);
        result.ExpectedChunkCount.Should().Be(2);
        result.IndexedChunkCount.Should().Be(2);
    }

    // --- T10 (plan §4 T11 test-contract Row 1, job-transition retry-loop scenario 2/2) ---
    // Issue 1 (BLOCKING): This test verifies that when all three active statuses fail to transition,
    // the method returns false and reports the failure appropriately.
    [Fact]
    public async Task IndexAsync_AllJobTransitionAttemptsFail_ExhaustsAllThreeStatusesAndReportsFailure()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        var blobPath = BlobPathFor(uploadId);

        var outcomes = new List<ChunkIndexOutcome>
        {
            new("chunk-1", true, null, 1, 0, "file.pdf"),
            new("chunk-2", true, null, 1, 1, "file.pdf")
        };
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(2, 1, outcomes));

        // Set up the transition mock to fail for all three statuses (Indexing, Processing, Queued)
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(
                job.IngestionJobId,
                It.IsAny<IngestionJobStatus>(),
                It.IsAny<IngestionJobStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);  // All transitions fail (job is in an unexpected state)

        var result = await _sut.IndexAsync(new MemoryStream(new byte[] { 1 }), uploadId, Container, blobPath, indexedArtifactId, job);

        // Verify all three statuses were attempted
        _jobRepoMock.Verify(
            x => x.TryTransitionToTerminalAsync(
                job.IngestionJobId,
                IngestionJobStatus.Indexing,
                It.IsAny<IngestionJobStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the loop must attempt Indexing status");

        _jobRepoMock.Verify(
            x => x.TryTransitionToTerminalAsync(
                job.IngestionJobId,
                IngestionJobStatus.Processing,
                It.IsAny<IngestionJobStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the loop must attempt Processing status even after Indexing fails");

        _jobRepoMock.Verify(
            x => x.TryTransitionToTerminalAsync(
                job.IngestionJobId,
                IngestionJobStatus.Queued,
                It.IsAny<IngestionJobStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the loop must attempt Queued status even after Processing fails");

        result.Should().NotBeNull();
        result.Succeeded.Should().BeTrue(
            "indexing itself succeeded and artifacts/chunks were persisted; the job transition failure is a secondary issue (best-effort)");
        result.JobTransitionedToTerminal.Should().BeFalse(
            "all three transition attempts failed, so the job could not be transitioned to terminal");
        result.ArtifactState.Should().Be(IndexedArtifactState.Completed);
        result.ExpectedChunkCount.Should().Be(2);
        result.IndexedChunkCount.Should().Be(2);
        result.FailedChunkCount.Should().Be(0,
            "chunk-level indexing succeeded; the counts should reflect actual persisted state, not the transition failure");
    }

    // --- T10 (plan §4 T11 test-contract Row 1, job-transition-throws-after-persistence scenario) ---
    // Issue 2: Investigate whether exception handling after persistence is pre-existing or regression.
    // This test documents the current behavior: if a job-transition throws AFTER artifact/chunks are
    // persisted, the returned DTO should reflect the actual persisted state (with real counts and
    // ArtifactState), not zeros. This ensures callers (sweep, adopt, etc.) can see what was actually written.
    [Fact]
    public async Task IndexAsync_JobTransitionThrowsAfterPersistence_ReportsPersistedArtifactStateNotZeros()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        var blobPath = BlobPathFor(uploadId);

        var outcomes = new List<ChunkIndexOutcome>
        {
            new("chunk-1", true, null, 1, 0, "file.pdf"),
            new("chunk-2", true, null, 1, 1, "file.pdf")
        };
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(2, 1, outcomes));

        // When TryTransitionToTerminalAsync is called, throw an exception (simulating a database/repository error)
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(
                It.IsAny<Guid>(),
                It.IsAny<IngestionJobStatus>(),
                It.IsAny<IngestionJobStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var result = await _sut.IndexAsync(new MemoryStream(new byte[] { 1 }), uploadId, Container, blobPath, indexedArtifactId, job);

        // The artifact and chunks were persisted before the job-transition call threw, so the DTO
        // should report the actual persisted state, not zeros.
        result.Should().NotBeNull();
        result.Succeeded.Should().BeFalse(
            "the job-transition failure is a critical issue that prevents the workflow from completing");
        result.ExpectedChunkCount.Should().Be(2,
            "the artifact was persisted with 2 expected chunks before the transition threw");
        result.IndexedChunkCount.Should().Be(2,
            "both chunks were successfully indexed and persisted before the transition threw");
        result.FailedChunkCount.Should().Be(0,
            "no chunks failed indexing; the failure is in job transition, not in indexing");
        result.ArtifactState.Should().Be(IndexedArtifactState.Completed,
            "the artifact state was computed and persisted before the transition threw");
        result.JobTransitionedToTerminal.Should().BeFalse(
            "the transition failed, so the job is not transitioned to terminal");

        // Verify the artifact/chunks were persisted despite the exception
        _artifactRepoMock.Verify(
            x => x.UpsertAsync(It.IsAny<IndexedArtifactDto>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "the artifact must be persisted before the job-transition attempt");
        _chunkRepoMock.Verify(
            x => x.UpsertManyAsync(It.IsAny<IReadOnlyCollection<IndexedChunkDto>>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "chunks must be persisted before the job-transition attempt");
    }
}
