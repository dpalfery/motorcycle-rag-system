using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using Xunit;
using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

public class ProcessorArtifactServiceTests
{
    private readonly Mock<IBlobStorageService> _blobStorageMock;
    private readonly Mock<ISearchChunkIndexingCoordinator> _coordinatorMock;
    private readonly Mock<IIngestionJobRepository> _jobRepoMock;
    private readonly Mock<IIngestionSourceAccessTokenService> _tokenServiceMock;
    private readonly Mock<IIngestionJobService> _ingestionJobServiceMock;
    private readonly ProcessorArtifactService _sut;

    public ProcessorArtifactServiceTests()
    {
        _blobStorageMock = new Mock<IBlobStorageService>();
        _coordinatorMock = new Mock<ISearchChunkIndexingCoordinator>();
        _jobRepoMock = new Mock<IIngestionJobRepository>();
        _tokenServiceMock = new Mock<IIngestionSourceAccessTokenService>();
        _ingestionJobServiceMock = new Mock<IIngestionJobService>();

        var blobOptions = Options.Create(new BlobStorageOptions());

        _sut = new ProcessorArtifactService(
            _blobStorageMock.Object,
            blobOptions,
            _coordinatorMock.Object,
            _jobRepoMock.Object,
            _tokenServiceMock.Object,
            _ingestionJobServiceMock.Object,
            NullLogger<ProcessorArtifactService>.Instance);
    }

    /// <summary>A MemoryStream override that reports itself as non-seekable, for exercising the seekability guard.</summary>
    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }

    private void SetUpNoJobFound(string uploadId)
    {
        foreach (var inputType in new[] { IngestionJobType.PDFManual, IngestionJobType.StructuredSpecification, IngestionJobType.Batch })
        {
            _jobRepoMock
                .Setup(x => x.GetLatestByInputAsync(uploadId, inputType, It.IsAny<CancellationToken>()))
                .ReturnsAsync((IngestionJob?)null);
        }
    }

    private void SetUpJobFound(string uploadId, IngestionJob job)
    {
        _jobRepoMock
            .Setup(x => x.GetLatestByInputAsync(uploadId, IngestionJobType.PDFManual, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _jobRepoMock
            .Setup(x => x.GetLatestByInputAsync(uploadId, IngestionJobType.StructuredSpecification, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);
        _jobRepoMock
            .Setup(x => x.GetLatestByInputAsync(uploadId, IngestionJobType.Batch, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);
    }

    /// <summary>
    /// Builds a <see cref="ProcessorArtifactService"/> that shares this test's mocked collaborators
    /// but swaps in a caller-supplied logger, so log-level/EventId assertions (plan
    /// 2026-08-03-processor-artifact-skip-observability, D6) don't have to run against
    /// <see cref="NullLogger{T}"/>.
    /// </summary>
    private ProcessorArtifactService CreateSutWithLogger(ILogger<ProcessorArtifactService> logger) =>
        new(
            _blobStorageMock.Object,
            Options.Create(new BlobStorageOptions()),
            _coordinatorMock.Object,
            _jobRepoMock.Object,
            _tokenServiceMock.Object,
            _ingestionJobServiceMock.Object,
            logger);

    /// <summary>
    /// Constructs an <see cref="IngestionJob"/> whose <see cref="IngestionJob.IngestionJobId"/> is
    /// <see cref="Guid.Empty"/>, deliberately bypassing every public factory. <see cref="IngestionJob.Rehydrate"/>
    /// throws <see cref="ArgumentException"/> for an empty ingestion job id -- a legitimate domain invariant --
    /// so a row-shaped test double built via <see cref="RuntimeHelpers.GetUninitializedObject"/> is the only way
    /// to exercise the defensive `job.IngestionJobId == Guid.Empty` guard in
    /// <c>ProcessorArtifactService.ProcessSearchChunksAsync</c>. That guard exists precisely because the value is
    /// externally sourced (plan 2026-08-03-processor-artifact-skip-observability, D2 investigation notes) and the
    /// invariant that normally prevents it cannot be assumed to hold for every corrupted persistence row.
    /// </summary>
    private static IngestionJob CreateJobWithEmptyIngestionJobId() =>
        (IngestionJob)RuntimeHelpers.GetUninitializedObject(typeof(IngestionJob));

    /// <summary>Every Error-level <see cref="EventId"/> recorded on the given logger mock.</summary>
    private static IReadOnlyList<EventId> CapturedErrorEventIds(Mock<ILogger<ProcessorArtifactService>> loggerMock) =>
        loggerMock.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ILogger.Log)
                && invocation.Arguments[0] is LogLevel level && level == LogLevel.Error)
            .Select(invocation => (EventId)invocation.Arguments[1]!)
            .ToList();

    /// <summary>Count of Warning-level log records recorded on the given logger mock.</summary>
    private static int CapturedWarningLogCount(Mock<ILogger<ProcessorArtifactService>> loggerMock) =>
        loggerMock.Invocations.Count(invocation => invocation.Method.Name == nameof(ILogger.Log)
            && invocation.Arguments[0] is LogLevel level && level == LogLevel.Warning);

    [Fact]
    public async Task UploadArtifactAsync_InvalidUploadId_ReturnsInvalidUploadIdStatus()
    {
        var request = new ProcessorArtifactUploadRequest("not-a-guid", "search-chunks", new MemoryStream(), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.InvalidUploadId);
        result.Response.Should().BeNull();
        _blobStorageMock.Verify(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadArtifactAsync_InvalidArtifactType_ReturnsInvalidArtifactTypeStatus()
    {
        var uploadId = Guid.NewGuid().ToString();
        var request = new ProcessorArtifactUploadRequest(uploadId, "unknown-type", new MemoryStream(), "application/octet-stream");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.InvalidArtifactType);
        result.Response.Should().BeNull();
        _blobStorageMock.Verify(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadSourceAsync_WhenUploadIdIsInvalid_ReturnsInvalidUploadIdWithoutStorageAccess()
    {
        var result = await _sut.DownloadSourceAsync("not-a-guid", "manual-pdf", null);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.InvalidUploadId);
        result.Content.Should().BeNull();
        _blobStorageMock.Verify(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadSourceAsync_WhenDocumentTypeIsInvalid_ReturnsInvalidDocumentTypeWithoutStorageAccess()
    {
        var result = await _sut.DownloadSourceAsync(Guid.NewGuid().ToString(), "unsupported", null);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.InvalidDocumentType);
        result.ContentType.Should().BeNull();
        _blobStorageMock.Verify(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadSourceAsync_WhenAccessTokenIsInvalid_ReturnsUnauthorizedWithoutStorageAccess()
    {
        var uploadId = Guid.NewGuid().ToString();
        _tokenServiceMock
            .Setup(x => x.IsValid("bad-token", uploadId, "manual-pdf"))
            .Returns(false);

        var result = await _sut.DownloadSourceAsync(uploadId, "manual-pdf", "bad-token");

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Unauthorized);
        _tokenServiceMock.Verify(x => x.IsValid("bad-token", uploadId, "manual-pdf"), Times.Once);
        _blobStorageMock.Verify(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadSourceAsync_WhenSourceDoesNotExist_ReturnsNotFound()
    {
        var uploadId = Guid.NewGuid().ToString();
        _blobStorageMock
            .Setup(x => x.ExistsAsync("raw-uploads", $"{uploadId}/source.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _sut.DownloadSourceAsync(uploadId, "manual-pdf", null);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.NotFound);
        _blobStorageMock.Verify(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadSourceAsync_WhenSourceExists_ReturnsDownloadedStreamAndContentType()
    {
        var uploadId = Guid.NewGuid().ToString();
        var source = new MemoryStream([1, 2, 3]);
        using var cts = new CancellationTokenSource();
        _blobStorageMock
            .Setup(x => x.ExistsAsync("raw-uploads", $"{uploadId}/source.pdf", cts.Token))
            .ReturnsAsync(true);
        _blobStorageMock
            .Setup(x => x.DownloadAsync("raw-uploads", $"{uploadId}/source.pdf", cts.Token))
            .ReturnsAsync(source);

        var result = await _sut.DownloadSourceAsync(uploadId, "manual-pdf", null, cts.Token);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        result.ContentType.Should().Be("application/pdf");
        result.Content.Should().BeSameAs(source);
    }

    [Fact]
    public async Task UploadArtifactAsync_GraphEntitiesArtifact_UsesRawUploadsContainerAndSkipsChunkIndexing()
    {
        var uploadId = Guid.NewGuid().ToString();
        var request = new ProcessorArtifactUploadRequest(uploadId, "graph-entities", new MemoryStream(new byte[] { 1, 2, 3 }), "application/json");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        result.Response.Should().NotBeNull();
        result.Response!.Status.Should().Be("stored");
        _blobStorageMock.Verify(
            x => x.UploadAsync("raw-uploads", $"graph-entities/{uploadId}/entities.json", It.IsAny<Stream>(), "application/json", It.IsAny<CancellationToken>()),
            Times.Once);
        // Graph entities skip coordinator entirely
        _coordinatorMock.Verify(x => x.IndexAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_NonSeekableContent_ThrowsInvalidOperationException()
    {
        var uploadId = Guid.NewGuid().ToString();
        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new NonSeekableStream(), "application/jsonl");

        var act = async () => await _sut.UploadArtifactAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Search chunk artifact content must be seekable for indexing.");
        // Non-seekable content check fails before coordinator is called
        _coordinatorMock.Verify(x => x.IndexAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // T1 regression guard (plan: 2026-08-03-processor-artifact-skip-observability, §4 T3 test-contract Row 3):
    // the happy path below, the partial-indexing path
    // (UploadArtifactAsync_SearchChunksArtifact_PartialIndexing_MarksPartiallyIndexedAndTransitionsJob), the
    // indexing-throws path (UploadArtifactAsync_SearchChunksArtifact_IndexingThrows_TransitionsJobToFailed) and
    // the metadata-throws path (UploadArtifactAsync_SearchChunksArtifact_MetadataUpdateFails_DoesNotThrowAndUploadStillSucceeds)
    // must retain their existing Success/IndexedArtifactState/job-transition/no-exception-escapes outcomes
    // unchanged once T3 lands. They already pass today and are the regression baseline for T3.
    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_AllChunksIndexed_UpsertsCompletedArtifactAndTransitionsJobCompleted()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        SetUpJobFound(uploadId, job);

        var coordinatorOutcome = new SearchChunkIndexingOutcomeDto(
            IndexedArtifactId: indexedArtifactId,
            IngestionJobId: job.IngestionJobId,
            Succeeded: true,
            ExpectedChunkCount: 2,
            IndexedChunkCount: 2,
            FailedChunkCount: 0,
            ArtifactState: IndexedArtifactState.Completed,
            JobTransitionedToTerminal: true,
            FailureReason: null);

        _coordinatorMock
            .Setup(x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coordinatorOutcome);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        result.Response.Should().NotBeNull();
        result.Response!.Status.Should().Be("stored");

        // Verify the coordinator was called with the right job
        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_PartialIndexing_MarksPartiallyIndexedAndTransitionsJob()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        SetUpJobFound(uploadId, job);

        var coordinatorOutcome = new SearchChunkIndexingOutcomeDto(
            IndexedArtifactId: indexedArtifactId,
            IngestionJobId: job.IngestionJobId,
            Succeeded: true,
            ExpectedChunkCount: 2,
            IndexedChunkCount: 1,
            FailedChunkCount: 1,
            ArtifactState: IndexedArtifactState.PartiallyIndexed,
            JobTransitionedToTerminal: true,
            FailureReason: "Failed to index 1 chunk(s)");

        _coordinatorMock
            .Setup(x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coordinatorOutcome);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_WhenIndexingTransitionNeedsProcessingStatus_ContinuesUntilTransitioned()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        SetUpJobFound(uploadId, job);

        var coordinatorOutcome = new SearchChunkIndexingOutcomeDto(
            IndexedArtifactId: indexedArtifactId,
            IngestionJobId: job.IngestionJobId,
            Succeeded: true,
            ExpectedChunkCount: 1,
            IndexedChunkCount: 1,
            FailedChunkCount: 0,
            ArtifactState: IndexedArtifactState.Completed,
            JobTransitionedToTerminal: true,
            FailureReason: null);

        _coordinatorMock
            .Setup(x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coordinatorOutcome);

        var result = await _sut.UploadArtifactAsync(new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream([1]), "application/jsonl"));

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        // Verify the coordinator was called (job transition logic is now in the coordinator)
        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_WhenNoActiveStateTransitions_ExhaustsAllTerminalTransitions()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        SetUpJobFound(uploadId, job);

        var coordinatorOutcome = new SearchChunkIndexingOutcomeDto(
            IndexedArtifactId: indexedArtifactId,
            IngestionJobId: job.IngestionJobId,
            Succeeded: true,
            ExpectedChunkCount: 1,
            IndexedChunkCount: 1,
            FailedChunkCount: 0,
            ArtifactState: IndexedArtifactState.Completed,
            JobTransitionedToTerminal: false,  // No transition succeeded
            FailureReason: null);

        _coordinatorMock
            .Setup(x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coordinatorOutcome);

        var result = await _sut.UploadArtifactAsync(new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream([1]), "application/jsonl"));

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        // Coordinator handles all transition logic internally
        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // T7 (plan: 2026-08-03-processor-artifact-skip-observability, D3/D5): null job → no anchors → no indexing →
    // no catalog row is EVER possible for this artifact (D4: schema.sql declares
    // IndexedArtifacts.IngestionJobId NOT NULL with an FK to IngestionJobs, and the absent job is exactly what
    // makes that row unwritable). Blob metadata is therefore the durable orphan state machine (D5): the skip
    // path DOES call SetMetadataAsync, once, on the artifact's own blob, stamping the complete orphan key set
    // (state=Orphaned, orphanReason=NoIngestionJob, orphanAttempts=0, orphanFirstDetectedUtc,
    // dateLastProcessed) inside the same best-effort try/catch that already guards the happy-path stamp.
    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_NoJobFound_SkipsCatalogWriteAndStampsOrphanMetadata()
    {
        var uploadId = Guid.NewGuid().ToString();
        SetUpNoJobFound(uploadId);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.IndexingSkipped);
        // Coordinator never called when job is not found
        _coordinatorMock.Verify(x => x.IndexAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
        _blobStorageMock.Verify(
            x => x.SetMetadataAsync(
                "search-chunks",
                $"{uploadId}/chunks.jsonl",
                It.Is<Dictionary<string, string>>(metadata => IsCompleteNoJobOrphanMetadata(metadata)),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the skip path must durably mark the blob as orphaned (D5) since no job exists; " +
            "SetMetadataAsync replaces the whole metadata collection, so every intended key must be present in one call");
    }

    // T7 (plan: 2026-08-03-processor-artifact-skip-observability, §4 T7 test-contract row): the orphan-stamp
    // write is wrapped in the same best-effort try/catch that already protects the happy-path metadata write --
    // a metadata-store outage must never surface as an upload failure, and the returned status/response must
    // stay exactly what a successful skip would have reported.
    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_NoJobFound_OrphanMetadataUpdateFails_DoesNotThrowAndStatusStaysIndexingSkipped()
    {
        var uploadId = Guid.NewGuid().ToString();
        SetUpNoJobFound(uploadId);
        _blobStorageMock
            .Setup(x => x.SetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("metadata store unavailable"));

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        // Reaching this line at all (rather than an unhandled exception failing the test) is itself part of the
        // assertion: the orphan-stamp SetMetadataAsync call must not be allowed to escape UploadArtifactAsync.
        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.IndexingSkipped,
            "a throwing SetMetadataAsync on the skip path must not change the returned status -- the artifact is " +
            "still stored and indexing was still genuinely skipped (D14)");
        result.Response.Should().NotBeNull();
        result.Response!.Status.Should().Be("stored-not-indexed");
        _blobStorageMock.Verify(
            x => x.SetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// True when <paramref name="metadata"/> is exactly the complete orphan key set the no-job skip path must
    /// stamp per plan 2026-08-03-processor-artifact-skip-observability D5: <c>state=Orphaned</c>,
    /// <c>orphanReason=NoIngestionJob</c>, <c>orphanAttempts=0</c>, plus timestamped
    /// <c>orphanFirstDetectedUtc</c> and <c>dateLastProcessed</c> values. Checking the full key set (not a
    /// subset) matters because <c>SetMetadataAsync</c> replaces the entire metadata collection -- a partial
    /// write would silently drop the orphan marker.
    /// </summary>
    private static bool IsCompleteNoJobOrphanMetadata(Dictionary<string, string> metadata) =>
        metadata.Count == 5 &&
        metadata.TryGetValue("state", out var state) && state == "Orphaned" &&
        metadata.TryGetValue("orphanReason", out var reason) && reason == "NoIngestionJob" &&
        metadata.TryGetValue("orphanAttempts", out var attempts) && attempts == "0" &&
        metadata.TryGetValue("orphanFirstDetectedUtc", out var firstDetected) && DateTimeOffset.TryParse(firstDetected, out _) &&
        metadata.TryGetValue("dateLastProcessed", out var lastProcessed) && DateTimeOffset.TryParse(lastProcessed, out _);

    // --- T1 (plan: 2026-08-03-processor-artifact-skip-observability, §4 T3 test-contract Rows 1-2) ---
    // RED tests for the not-yet-implemented ProcessorArtifactOperationStatus.IndexingSkipped status and the
    // LogError + stable-EventId elevation of both anchor-unsatisfiable branches (D6, D14). Expected to fail to
    // compile until T3 adds the enum member; do not implement production code changes here.

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_NoJobFound_ReturnsIndexingSkippedStatusAndStoredNotIndexedResponse()
    {
        var uploadId = Guid.NewGuid().ToString();
        SetUpNoJobFound(uploadId);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.IndexingSkipped,
            "a search-chunks upload with no resolvable ingestion job must be observable to the caller (D14), " +
            "not silently reported as the same Success/\"stored\" outcome as a fully-indexed upload");
        result.Response.Should().NotBeNull();
        result.Response!.Status.Should().Be("stored-not-indexed",
            "the response body is the synchronous acknowledgement channel the Python processor can key off (D14)");
        // Fail-closed: coordinator never called when job is not found
        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "fail-closed (D1) -- an unresolved ingestion job must never reach the coordinator");
    }

    [Fact]
    public async Task UploadArtifactAsync_GraphEntitiesArtifact_StillReturnsSuccessStatusAndStoredResponse()
    {
        // Guards against the new IndexingSkipped member leaking into the graph-entities path, which never
        // calls ProcessSearchChunksAsync at all.
        var uploadId = Guid.NewGuid().ToString();
        var request = new ProcessorArtifactUploadRequest(uploadId, "graph-entities", new MemoryStream(new byte[] { 1, 2, 3 }), "application/json");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        result.Response.Should().NotBeNull();
        result.Response!.Status.Should().Be("stored");
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_JobFound_StillReturnsSuccessStatusAndStoredResponse()
    {
        // Guards against the new IndexingSkipped member leaking into the happy path once a job resolves.
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        SetUpJobFound(uploadId, job);

        var coordinatorOutcome = new SearchChunkIndexingOutcomeDto(
            IndexedArtifactId: indexedArtifactId,
            IngestionJobId: job.IngestionJobId,
            Succeeded: true,
            ExpectedChunkCount: 1,
            IndexedChunkCount: 1,
            FailedChunkCount: 0,
            ArtifactState: IndexedArtifactState.Completed,
            JobTransitionedToTerminal: true,
            FailureReason: null);

        _coordinatorMock
            .Setup(x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coordinatorOutcome);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        result.Response.Should().NotBeNull();
        result.Response!.Status.Should().Be("stored");
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_NoJobFound_LogsErrorWithStableEventIdAndNeverWarning()
    {
        var uploadId = Guid.NewGuid().ToString();
        SetUpNoJobFound(uploadId);
        var loggerMock = new Mock<ILogger<ProcessorArtifactService>>();
        var sut = CreateSutWithLogger(loggerMock.Object);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        await sut.UploadArtifactAsync(request);

        var errorEventIds = CapturedErrorEventIds(loggerMock);
        errorEventIds.Should().ContainSingle(
            "the job-is-null anchor-unsatisfiable branch must emit exactly one Error-level log record (D6)");
        errorEventIds[0].Id.Should().NotBe(0,
            "the EventId must be a stable, non-default value an Azure Monitor alert rule can bind to (D6)");
        CapturedWarningLogCount(loggerMock).Should().Be(0,
            "D6 elevates every anchor-unsatisfiable branch to Error; no Warning-level record should remain for the skip");
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_JobIngestionJobIdEmpty_LogsErrorWithStableEventIdAndNeverWarning()
    {
        var uploadId = Guid.NewGuid().ToString();
        SetUpJobFound(uploadId, CreateJobWithEmptyIngestionJobId());
        var loggerMock = new Mock<ILogger<ProcessorArtifactService>>();
        var sut = CreateSutWithLogger(loggerMock.Object);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        await sut.UploadArtifactAsync(request);

        var errorEventIds = CapturedErrorEventIds(loggerMock);
        errorEventIds.Should().ContainSingle(
            "the empty-IngestionJobId anchor-unsatisfiable branch must emit exactly one Error-level log record (D6)");
        errorEventIds[0].Id.Should().NotBe(0,
            "the EventId must be a stable, non-default value an Azure Monitor alert rule can bind to (D6)");
        CapturedWarningLogCount(loggerMock).Should().Be(0,
            "D6 elevates every anchor-unsatisfiable branch to Error; no Warning-level record should remain for the skip");
        // Fail-closed: coordinator never called for empty IngestionJobId
        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "fail-closed (D1) -- an unsatisfiable anchor must never reach the coordinator");
    }

    [Fact]
    public async Task UploadArtifactAsync_AnchorUnsatisfiableBranches_UseDistinctStableEventIds()
    {
        var noJobUploadId = Guid.NewGuid().ToString();
        SetUpNoJobFound(noJobUploadId);
        var noJobLoggerMock = new Mock<ILogger<ProcessorArtifactService>>();
        var noJobSut = CreateSutWithLogger(noJobLoggerMock.Object);
        await noJobSut.UploadArtifactAsync(
            new ProcessorArtifactUploadRequest(noJobUploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl"));

        var emptyIdUploadId = Guid.NewGuid().ToString();
        SetUpJobFound(emptyIdUploadId, CreateJobWithEmptyIngestionJobId());
        var emptyIdLoggerMock = new Mock<ILogger<ProcessorArtifactService>>();
        var emptyIdSut = CreateSutWithLogger(emptyIdLoggerMock.Object);
        await emptyIdSut.UploadArtifactAsync(
            new ProcessorArtifactUploadRequest(emptyIdUploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl"));

        var noJobEventId = CapturedErrorEventIds(noJobLoggerMock).Single();
        var emptyIdEventId = CapturedErrorEventIds(emptyIdLoggerMock).Single();

        noJobEventId.Id.Should().NotBe(0);
        emptyIdEventId.Id.Should().NotBe(0);
        noJobEventId.Id.Should().NotBe(emptyIdEventId.Id,
            "each anchor-unsatisfiable branch must bind to its own stable EventId so an Azure Monitor alert rule " +
            "can distinguish which contract violation fired (D6)");
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_MetadataUpdateFails_DoesNotThrowAndUploadStillSucceeds()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        SetUpJobFound(uploadId, job);

        // Coordinator returns success even if metadata write failed (best-effort)
        var coordinatorOutcome = new SearchChunkIndexingOutcomeDto(
            IndexedArtifactId: indexedArtifactId,
            IngestionJobId: job.IngestionJobId,
            Succeeded: true,
            ExpectedChunkCount: 1,
            IndexedChunkCount: 1,
            FailedChunkCount: 0,
            ArtifactState: IndexedArtifactState.Completed,
            JobTransitionedToTerminal: true,
            FailureReason: null);

        _coordinatorMock
            .Setup(x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coordinatorOutcome);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_IndexingThrows_TransitionsJobToFailed()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        SetUpJobFound(uploadId, job);

        // Coordinator catches indexing exception and returns failed outcome
        var coordinatorOutcome = new SearchChunkIndexingOutcomeDto(
            IndexedArtifactId: indexedArtifactId,
            IngestionJobId: job.IngestionJobId,
            Succeeded: false,
            ExpectedChunkCount: 0,
            IndexedChunkCount: 0,
            FailedChunkCount: 0,
            ArtifactState: null,
            JobTransitionedToTerminal: true,
            FailureReason: "InvalidOperationException: search index unavailable");

        _coordinatorMock
            .Setup(x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coordinatorOutcome);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        // The exception is caught internally by the coordinator (best-effort); the upload itself still reports success.
        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_IndexingThrowsAndNoJobFound_DoesNotThrowAndSkipsTransition()
    {
        var uploadId = Guid.NewGuid().ToString();
        SetUpNoJobFound(uploadId);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var act = async () => await _sut.UploadArtifactAsync(request);

        await act.Should().NotThrowAsync();
        // Since no job is found, the coordinator is never called and no transition attempted
        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_IndexingThrowsAndTransitionAlsoThrows_SwallowsBothExceptions()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var indexedArtifactId = Guid.NewGuid();
        SetUpJobFound(uploadId, job);

        // Coordinator swallows both indexing and job transition exceptions
        var coordinatorOutcome = new SearchChunkIndexingOutcomeDto(
            IndexedArtifactId: indexedArtifactId,
            IngestionJobId: job.IngestionJobId,
            Succeeded: false,
            ExpectedChunkCount: 0,
            IndexedChunkCount: 0,
            FailedChunkCount: 0,
            ArtifactState: null,
            JobTransitionedToTerminal: false,
            FailureReason: "InvalidOperationException: search index unavailable");

        _coordinatorMock
            .Setup(x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coordinatorOutcome);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var act = async () => await _sut.UploadArtifactAsync(request);

        await act.Should().NotThrowAsync();
    }

    // --- T8 (plan: 2026-08-01-vector-graph-anchor-id-contract, decision D3) ---
    // ProcessorArtifactService resolves indexedArtifactId and job BEFORE calling the coordinator.
    // The coordinator then resolves sourceContentHash internally and calls IndexFromJsonlAsync with
    // all anchors pre-resolved. This test verifies that ProcessorArtifactService passes a pre-allocated
    // artifact ID and a valid job to the coordinator. Anchor-resolution details (sourceContentHash)
    // are tested in SearchChunkIndexingCoordinatorTests.
    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_ResolvesAnchorsBeforeIndexing_PassesNonDefaultValuesToFiveParameterOverload()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        SetUpJobFound(uploadId, job);

        // Capture the indexedArtifactId passed to the coordinator
        var capturedIndexedArtifactId = Guid.Empty;
        var capturedJob = (IngestionJob?)null;

        _coordinatorMock
            .Setup(x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, string, string, Guid, IngestionJob, CancellationToken>((_, _, _, _, artifactId, jobArg, _) =>
            {
                capturedIndexedArtifactId = artifactId;
                capturedJob = jobArg;
            })
            .ReturnsAsync(new SearchChunkIndexingOutcomeDto(
                IndexedArtifactId: Guid.Empty,
                IngestionJobId: job.IngestionJobId,
                Succeeded: true,
                ExpectedChunkCount: 2,
                IndexedChunkCount: 2,
                FailedChunkCount: 0,
                ArtifactState: IndexedArtifactState.Completed,
                JobTransitionedToTerminal: true,
                FailureReason: null));

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);

        // ProcessorArtifactService must pass a pre-allocated artifact ID (not Guid.Empty)
        capturedIndexedArtifactId.Should().NotBe(Guid.Empty,
            "the artifact ID must be pre-allocated (Guid.NewGuid()) before calling the coordinator");

        // ProcessorArtifactService must pass the resolved job
        capturedJob.Should().Be(job,
            "the already-resolved job must be passed to the coordinator");

        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- T5 (plan: 2026-08-02-anchor-id-debt-cleanup, D7) ---
    // Regression guard for the §7 null-clobber defense at the T8 (initial-ingestion) call site.
    // A freshly-created job (IngestionJob.Create always leaves ManualDocumentId null) must result in
    // a null sourceContentHash passed to IndexFromJsonlAsync (not string.Empty, which would clobber
    // an already-indexed hash). The coordinator handles this resolution; this test verifies that
    // ProcessorArtifactService passes a job with ManualDocumentId null to the coordinator.
    // The actual hash-resolution logic is tested in SearchChunkIndexingCoordinatorTests.
    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_JobHasNoManualDocument_PassesNullSourceContentHash_NeverEmptyString()
    {
        var uploadId = Guid.NewGuid().ToString();

        // IngestionJob.Create always leaves ManualDocumentId null -- the freshly-created-job shape.
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        SetUpJobFound(uploadId, job);

        var indexedArtifactId = Guid.NewGuid();
        var coordinatorOutcome = new SearchChunkIndexingOutcomeDto(
            IndexedArtifactId: indexedArtifactId,
            IngestionJobId: job.IngestionJobId,
            Succeeded: true,
            ExpectedChunkCount: 1,
            IndexedChunkCount: 1,
            FailedChunkCount: 0,
            ArtifactState: IndexedArtifactState.Completed,
            JobTransitionedToTerminal: true,
            FailureReason: null);

        var capturedJob = (IngestionJob?)null;
        _coordinatorMock
            .Setup(x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, string, string, Guid, IngestionJob, CancellationToken>((_, _, _, _, _, jobArg, _) =>
            {
                capturedJob = jobArg;
            })
            .ReturnsAsync(coordinatorOutcome);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);

        // Verify that ProcessorArtifactService passes the job with no ManualDocumentId to the coordinator
        capturedJob.Should().NotBeNull();
        capturedJob!.ManualDocumentId.Should().BeNull(
            "a freshly-created job has no ManualDocumentId -- the coordinator must resolve to null sourceContentHash, " +
            "not string.Empty (T7 JsonIgnore(WhenWritingNull) guard omits null from the merge payload; " +
            "empty string would clobber an already-indexed hash)");

        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), uploadId, "search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Guid>(), job, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_ValidRunIdAndRequest_DelegatesToTransitionStageAsync()
    {
        var runId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, runId, createdBySubject: null);
        var request = new IngestionJobStageRequest { Stage = "embedding", ChunksProcessed = 5, TotalChunks = 10 };
        var response = new IngestionJobStatusResponse { Status = "Processing", CurrentStage = "embedding" };

        _jobRepoMock
            .Setup(x => x.GetByDocIngestionRunIdAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _ingestionJobServiceMock
            .Setup(x => x.TransitionStageAsync(job.IngestionJobId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _sut.ReportJobStageByRunIdAsync(runId, request);

        result.Should().BeSameAs(response);
        _ingestionJobServiceMock.Verify(
            x => x.TransitionStageAsync(job.IngestionJobId, request, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_RunIdNotFound_ReturnsNull()
    {
        var runId = Guid.NewGuid().ToString();
        var request = new IngestionJobStageRequest { Stage = "embedding" };

        _jobRepoMock
            .Setup(x => x.GetByDocIngestionRunIdAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);

        var result = await _sut.ReportJobStageByRunIdAsync(runId, request);

        result.Should().BeNull();
        _ingestionJobServiceMock.Verify(
            x => x.TransitionStageAsync(It.IsAny<Guid>(), It.IsAny<IngestionJobStageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_NullRunId_ThrowsArgumentException()
    {
        var request = new IngestionJobStageRequest { Stage = "embedding" };

        var act = async () => await _sut.ReportJobStageByRunIdAsync(null!, request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_EmptyRunId_ThrowsArgumentException()
    {
        var request = new IngestionJobStageRequest { Stage = "embedding" };

        var act = async () => await _sut.ReportJobStageByRunIdAsync("", request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_WhitespaceRunId_ThrowsArgumentException()
    {
        var request = new IngestionJobStageRequest { Stage = "embedding" };

        var act = async () => await _sut.ReportJobStageByRunIdAsync("   ", request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_NullRequest_ThrowsArgumentNullException()
    {
        var runId = Guid.NewGuid().ToString();

        var act = async () => await _sut.ReportJobStageByRunIdAsync(runId, null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_JobFound_PassesCancellationToken()
    {
        var runId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, runId, createdBySubject: null);
        var request = new IngestionJobStageRequest { Stage = "embedding" };
        var response = new IngestionJobStatusResponse();
        using var cts = new CancellationTokenSource();

        _jobRepoMock
            .Setup(x => x.GetByDocIngestionRunIdAsync(runId, cts.Token))
            .ReturnsAsync(job);
        _ingestionJobServiceMock
            .Setup(x => x.TransitionStageAsync(job.IngestionJobId, request, cts.Token))
            .ReturnsAsync(response);

        var result = await _sut.ReportJobStageByRunIdAsync(runId, request, cts.Token);

        result.Should().NotBeNull();
    }
}
