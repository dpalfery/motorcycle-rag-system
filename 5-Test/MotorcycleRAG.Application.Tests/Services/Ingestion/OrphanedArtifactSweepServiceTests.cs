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
using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

/// <summary>
/// RED-phase tests for the not-yet-implemented <see cref="OrphanedArtifactSweepService"/> /
/// <see cref="IOrphanedArtifactSweepService"/> (plan 2026-08-03-processor-artifact-skip-observability, T12/T13,
/// decisions D7-D10, D12).
/// </summary>
/// <remarks>
/// This file defines the full contract T13's implementer must satisfy exactly. It re-drives orphaned
/// <c>search-chunks</c> artifacts (plan §3 Steps 3-4): a blob whose job now exists is healed through the shared
/// <see cref="ISearchChunkIndexingCoordinator"/> (T11); a blob whose job is still absent has its attempt count
/// bumped and the complete orphan key set rewritten (the "SetMetadataAsync replaces the entire collection" trap
/// noted in the plan); and a blob that exhausts either the attempt budget or the retention window is stamped
/// terminal and excluded from all future sweeps (D8), never deleted (D10).
/// <para>Sweep contract designed for T13's implementer:</para>
/// <code>
/// namespace MotorcycleRAG.Contracts.Interfaces;
///
/// public interface IOrphanedArtifactSweepService
/// {
///     /// &lt;summary&gt;
///     /// Runs one reconciliation cycle over the search-chunks container's Orphaned/OrphanedTerminal blobs.
///     /// Never throws: every per-blob failure (job-resolution exception, metadata-write exception) is caught,
///     /// counted in the returned DTO's Errored total, and does not abort the remaining blobs in the cycle.
///     /// &lt;/summary&gt;
///     Task&lt;OrphanSweepResultDto&gt; RunSweepAsync(CancellationToken cancellationToken = default);
/// }
/// </code>
/// <para>
/// Result/listing DTOs (Contracts.Models.DTOs.Ingestion, "Dto" suffix -- property-bag result carriers with no
/// domain invariant of their own; <see cref="OrphanedArtifactDto"/> doubles as the shape T17's later
/// <c>GET /api/ingestion/artifacts/orphaned</c> admin listing serializes over HTTP):
/// </para>
/// <code>
/// namespace MotorcycleRAG.Contracts.Models.DTOs.Ingestion;
///
/// public sealed record OrphanedArtifactDto(
///     string UploadId,
///     string Container,
///     string BlobPath,
///     string State,
///     string? OrphanReason,
///     int OrphanAttempts,
///     DateTimeOffset? OrphanFirstDetectedUtc);
///
/// public sealed record OrphanSweepResultDto(
///     int OrphansFound,
///     int Healed,
///     int AttemptsIncremented,
///     int TerminalTransitions,
///     int Errored);
/// </code>
/// <para>
/// New <see cref="IngestionOptions"/> members (Core, bound from the "Ingestion" config section, D9 defaults):
/// </para>
/// <code>
/// public int MaxOrphanRetryAttempts { get; set; } = 5;
/// public TimeSpan OrphanRetentionWindow { get; set; } = TimeSpan.FromHours(24);
/// public TimeSpan OrphanSweepInterval { get; set; } = TimeSpan.FromMinutes(5);
/// </code>
/// <para>
/// <c>OrphanedArtifactSweepService</c> constructor shape (Application/Services/Ingestion), mirroring the
/// constructor-injected, no-HTTP/SQL-in-Application shape of <c>ProcessorArtifactService</c>/
/// <c>SearchChunkIndexingCoordinator</c>:
/// </para>
/// <code>
/// public OrphanedArtifactSweepService(
///     IBlobStorageService blobStorageService,
///     ISearchChunkIndexingCoordinator searchChunkIndexingCoordinator,
///     IIngestionJobRepository jobRepository,
///     IOptions&lt;IngestionOptions&gt; ingestionOptions,
///     ILogger&lt;OrphanedArtifactSweepService&gt; logger)
/// </code>
/// <para><b>Algorithm each cycle (plan §3 Steps 3-4, in order):</b></para>
/// <list type="number">
/// <item>List the <c>search-chunks</c> container (the same literal container name
/// <c>ProcessorArtifactService.SearchChunksArtifact.ContainerName</c> already uses -- reuse it, do not
/// reintroduce a parallel literal or option) via <see cref="IBlobStorageService.ListAsync"/>, which returns
/// metadata per blob since T9.</item>
/// <item>For each blob, read the <c>state</c> metadata key.
///   <list type="bullet">
///   <item><c>"OrphanedTerminal"</c> -&gt; skip entirely: no job-resolution call, no metadata write, not counted
///   in <c>OrphansFound</c> (D8: terminal blobs are excluded from all future sweeps).</item>
///   <item>Any value other than <c>"Orphaned"</c> or <c>"OrphanedTerminal"</c> (including a missing <c>state</c>
///   key) -&gt; ignored entirely, same as above.</item>
///   <item><c>"Orphaned"</c> -&gt; counted in <c>OrphansFound</c> and processed per the steps below, inside a
///   per-blob try/catch: an exception from job resolution or the metadata write increments <c>Errored</c> and
///   moves on to the next blob without aborting the cycle.</item>
///   </list>
/// </item>
/// <item>Parse <c>uploadId</c> as the path segment before the first <c>/</c> in the blob's <c>Name</c> (mirrors
/// <c>TryGetArtifactLocation</c>'s <c>"{uploadId}/chunks.jsonl"</c> shape) and resolve the job using the exact
/// same three-input-type-in-order pattern as <c>ProcessorArtifactService.FindLatestSearchChunkJobAsync</c>
/// (<see cref="IngestionJobType.PDFManual"/>, then <see cref="IngestionJobType.StructuredSpecification"/>, then
/// <see cref="IngestionJobType.Batch"/>; take the one with the latest <c>CreatedAtUtc</c>).</item>
/// <item><b>Job found (heal):</b> download the blob and call
/// <see cref="ISearchChunkIndexingCoordinator.IndexAsync"/> with a freshly-allocated
/// <c>Guid.NewGuid()</c> <c>indexedArtifactId</c> (never <c>Guid.Empty</c>, per D1) and the resolved job. The
/// coordinator's own happy-path metadata stamp overwrites the orphan keys -- the sweep does not separately call
/// <see cref="IBlobStorageService.SetMetadataAsync"/> for a healed blob. Counts toward <c>Healed</c>.</item>
/// <item><b>Job absent:</b> this cycle counts as one more failed resolution attempt, so
/// <c>newAttempts = currentAttempts + 1</c> BEFORE the terminal checks below.
///   <list type="number">
///   <item>If <c>now - orphanFirstDetectedUtc &gt;= OrphanRetentionWindow</c> (checked first, independently of
///   the attempt count) -&gt; terminal, <c>orphanReason = "NoIngestionJobWithinRetentionWindow"</c>.</item>
///   <item>Else if <c>newAttempts &gt;= MaxOrphanRetryAttempts</c> -&gt; terminal,
///   <c>orphanReason = "NoIngestionJobAfterMaxAttempts"</c>.</item>
///   <item>Else -&gt; plain increment: <c>state</c> stays <c>"Orphaned"</c>, <c>orphanReason</c> stays
///   <c>"NoIngestionJob"</c>.</item>
///   </list>
/// In every one of these three outcomes, <see cref="IBlobStorageService.SetMetadataAsync"/> is called exactly
/// once with the COMPLETE 5-key set (<c>state</c>, <c>orphanReason</c>, <c>orphanAttempts</c> =
/// <c>newAttempts</c>, <c>orphanFirstDetectedUtc</c> preserved unchanged from the original value,
/// <c>dateLastProcessed</c> = now) -- never a partial update. The two terminal outcomes additionally log
/// <see cref="LogLevel.Error"/> with a stable <see cref="EventId"/>: <c>1003</c>
/// (<c>"NoIngestionJobAfterMaxAttempts"</c>) for the attempts branch, <c>1004</c>
/// (<c>"NoIngestionJobWithinRetentionWindow"</c>) for the window branch -- continuing the <c>100x</c> sequence
/// after <c>ProcessorArtifactService</c>'s existing <c>1001</c>/<c>1002</c>, confirmed collision-free by
/// repository-wide search. Terminal outcomes count toward <c>TerminalTransitions</c>; the plain-increment outcome
/// counts toward <c>AttemptsIncremented</c>.</item>
/// <item><see cref="IBlobStorageService.DeleteIfExistsAsync"/> (or any other removal API) is never called in any
/// branch, including terminal (D10) -- the blob is the only surviving copy of paid-for chunking work.</item>
/// </list>
/// </remarks>
public class OrphanedArtifactSweepServiceTests
{
    private const string Container = "search-chunks";

    private const string MetaState = "state";
    private const string MetaOrphanReason = "orphanReason";
    private const string MetaOrphanAttempts = "orphanAttempts";
    private const string MetaOrphanFirstDetectedUtc = "orphanFirstDetectedUtc";
    private const string MetaDateLastProcessed = "dateLastProcessed";

    private const string StateOrphaned = "Orphaned";
    private const string StateOrphanedTerminal = "OrphanedTerminal";
    private const string ReasonNoJob = "NoIngestionJob";
    private const string ReasonMaxAttempts = "NoIngestionJobAfterMaxAttempts";
    private const string ReasonRetentionWindow = "NoIngestionJobWithinRetentionWindow";

    private readonly Mock<IBlobStorageService> _blobStorageMock;
    private readonly Mock<ISearchChunkIndexingCoordinator> _coordinatorMock;
    private readonly Mock<IIngestionJobRepository> _jobRepoMock;
    private readonly IngestionOptions _ingestionOptions;
    private readonly OrphanedArtifactSweepService _sut;

    public OrphanedArtifactSweepServiceTests()
    {
        _blobStorageMock = new Mock<IBlobStorageService>();
        _coordinatorMock = new Mock<ISearchChunkIndexingCoordinator>();
        _jobRepoMock = new Mock<IIngestionJobRepository>();
        _ingestionOptions = new IngestionOptions
        {
            MaxOrphanRetryAttempts = 5,
            OrphanRetentionWindow = TimeSpan.FromHours(24),
            OrphanSweepInterval = TimeSpan.FromMinutes(5)
        };

        _sut = new OrphanedArtifactSweepService(
            _blobStorageMock.Object,
            _coordinatorMock.Object,
            _jobRepoMock.Object,
            Options.Create(_ingestionOptions),
            NullLogger<OrphanedArtifactSweepService>.Instance);
    }

    private OrphanedArtifactSweepService CreateSutWithLogger(ILogger<OrphanedArtifactSweepService> logger) =>
        new(
            _blobStorageMock.Object,
            _coordinatorMock.Object,
            _jobRepoMock.Object,
            Options.Create(_ingestionOptions),
            logger);

    private static string BlobPathFor(string uploadId) => $"{uploadId}/chunks.jsonl";

    private static BlobObjectDescriptor OrphanBlob(
        string uploadId,
        int attempts,
        DateTimeOffset firstDetectedUtc,
        DateTimeOffset? lastProcessedUtc = null) =>
        new()
        {
            Name = BlobPathFor(uploadId),
            ContentType = "application/jsonl",
            SizeBytes = 256,
            LastModifiedUtc = lastProcessedUtc ?? firstDetectedUtc,
            Metadata = new Dictionary<string, string>
            {
                [MetaState] = StateOrphaned,
                [MetaOrphanReason] = ReasonNoJob,
                [MetaOrphanAttempts] = attempts.ToString(),
                [MetaOrphanFirstDetectedUtc] = firstDetectedUtc.UtcDateTime.ToString("O"),
                [MetaDateLastProcessed] = (lastProcessedUtc ?? firstDetectedUtc).UtcDateTime.ToString("O")
            }
        };

    private static BlobObjectDescriptor TerminalBlob(
        string uploadId,
        string reason,
        int attempts,
        DateTimeOffset firstDetectedUtc) =>
        new()
        {
            Name = BlobPathFor(uploadId),
            ContentType = "application/jsonl",
            SizeBytes = 256,
            LastModifiedUtc = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>
            {
                [MetaState] = StateOrphanedTerminal,
                [MetaOrphanReason] = reason,
                [MetaOrphanAttempts] = attempts.ToString(),
                [MetaOrphanFirstDetectedUtc] = firstDetectedUtc.UtcDateTime.ToString("O"),
                [MetaDateLastProcessed] = DateTimeOffset.UtcNow.UtcDateTime.ToString("O")
            }
        };

    private static BlobObjectDescriptor NonOrphanBlob(string uploadId, string? state) =>
        new()
        {
            Name = BlobPathFor(uploadId),
            ContentType = "application/jsonl",
            SizeBytes = 256,
            LastModifiedUtc = DateTimeOffset.UtcNow,
            Metadata = state is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { [MetaState] = state }
        };

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

    private void SetUpNoJobFound(string uploadId)
    {
        foreach (var inputType in new[] { IngestionJobType.PDFManual, IngestionJobType.StructuredSpecification, IngestionJobType.Batch })
        {
            _jobRepoMock
                .Setup(x => x.GetLatestByInputAsync(uploadId, inputType, It.IsAny<CancellationToken>()))
                .ReturnsAsync((IngestionJob?)null);
        }
    }

    /// <summary>
    /// Constructs an <see cref="IngestionJob"/> whose <see cref="IngestionJob.IngestionJobId"/> is
    /// <see cref="Guid.Empty"/>, deliberately bypassing every public factory. <see cref="IngestionJob.Rehydrate"/>
    /// throws <see cref="ArgumentException"/> for an empty ingestion job id -- a legitimate domain invariant --
    /// so a row-shaped test double built via <see cref="RuntimeHelpers.GetUninitializedObject"/> is the only way
    /// to exercise the defensive `job.IngestionJobId == Guid.Empty` guard in
    /// <c>OrphanedArtifactSweepService.RunSweepAsync</c>. That guard exists precisely because the value is
    /// externally sourced (plan 2026-08-03-processor-artifact-skip-observability, D2 investigation notes) and the
    /// invariant that normally prevents it cannot be assumed to hold for every corrupted persistence row.
    /// </summary>
    private static IngestionJob CreateJobWithEmptyIngestionJobId() =>
        (IngestionJob)RuntimeHelpers.GetUninitializedObject(typeof(IngestionJob));

    /// <summary>Every Error-level <see cref="EventId"/> recorded on the given logger mock.</summary>
    private static IReadOnlyList<EventId> CapturedErrorEventIds(Mock<ILogger<OrphanedArtifactSweepService>> loggerMock) =>
        loggerMock.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ILogger.Log)
                && invocation.Arguments[0] is LogLevel level && level == LogLevel.Error)
            .Select(invocation => (EventId)invocation.Arguments[1]!)
            .ToList();

    private static SearchChunkIndexingOutcomeDto SucceededOutcome(Guid indexedArtifactId, Guid jobId) =>
        new(indexedArtifactId, jobId, true, 2, 2, 0, IndexedArtifactState.Completed, true, null);

    // --- (a) Heal: job now exists -> coordinator invoked with real anchors, orphan healed ---

    [Fact]
    public async Task RunSweepAsync_OrphanedBlobWithJobNowResolvable_InvokesCoordinatorWithRealAnchorsAndReportsHealed()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        var blob = OrphanBlob(uploadId, attempts: 2, firstDetectedUtc: DateTimeOffset.UtcNow.AddHours(-1));

        _blobStorageMock.Setup(x => x.ListAsync(Container, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { blob });
        SetUpJobFound(uploadId, job);

        using var downloadStream = new MemoryStream(new byte[] { 1, 2, 3 });
        _blobStorageMock
            .Setup(x => x.DownloadAsync(Container, blob.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(downloadStream);

        Guid capturedIndexedArtifactId = Guid.Empty;
        IngestionJob? capturedJob = null;
        _coordinatorMock
            .Setup(x => x.IndexAsync(
                It.IsAny<Stream>(), uploadId, Container, blob.Name, It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, string, string, Guid, IngestionJob, CancellationToken>(
                (_, _, _, _, artifactId, resolvedJob, _) =>
                {
                    capturedIndexedArtifactId = artifactId;
                    capturedJob = resolvedJob;
                })
            .ReturnsAsync((Stream _, string _, string _, string _, Guid artifactId, IngestionJob resolvedJob, CancellationToken _) =>
                SucceededOutcome(artifactId, resolvedJob.IngestionJobId));

        var result = await _sut.RunSweepAsync();

        capturedIndexedArtifactId.Should().NotBe(Guid.Empty,
            "the sweep must allocate a real anchor for the re-drive attempt, never Guid.Empty (D1)");
        capturedJob.Should().NotBeNull();
        capturedJob!.IngestionJobId.Should().Be(job.IngestionJobId,
            "the coordinator must be invoked with the job the sweep just resolved, not a synthetic one");

        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), uploadId, Container, blob.Name, It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // The coordinator owns the happy-path metadata stamp (T11); the sweep must not separately stamp orphan
        // keys for a healed blob.
        _blobStorageMock.Verify(
            x => x.SetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "healing delegates the metadata stamp entirely to the coordinator's own happy-path stamp");

        _blobStorageMock.Verify(
            x => x.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "(h) orphan blobs are only ever metadata-stamped, never removed");

        result.OrphansFound.Should().Be(1);
        result.Healed.Should().Be(1);
        result.AttemptsIncremented.Should().Be(0);
        result.TerminalTransitions.Should().Be(0);
        result.Errored.Should().Be(0);
    }

    // --- (a-special) Heal: job resolved with empty IngestionJobId (corrupted row) -> treated as job-absent, never healed ---

    [Fact]
    public async Task RunSweepAsync_OrphanedBlobWithResolvedJobHavingEmptyIngestionJobId_NeverCallsCoordinatorAndTreatsAsJobAbsent()
    {
        var uploadId = Guid.NewGuid().ToString();
        var jobWithEmptyId = CreateJobWithEmptyIngestionJobId();
        var blob = OrphanBlob(uploadId, attempts: 0, firstDetectedUtc: DateTimeOffset.UtcNow.AddHours(-1));

        _blobStorageMock.Setup(x => x.ListAsync(Container, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { blob });
        SetUpJobFound(uploadId, jobWithEmptyId);

        Dictionary<string, string>? capturedMetadata = null;
        _blobStorageMock
            .Setup(x => x.SetMetadataAsync(Container, blob.Name, It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, string>, CancellationToken>((_, _, metadata, _) => capturedMetadata = metadata)
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<OrphanedArtifactSweepService>>();
        var sut = CreateSutWithLogger(loggerMock.Object);

        var result = await sut.RunSweepAsync();

        // The coordinator must NEVER be called when the job has empty IngestionJobId (D1/D2)
        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "fail-closed (D1) -- an unsatisfiable anchor (empty IngestionJobId) must never reach the coordinator");

        // The blob should be treated as job-absent: attempts incremented (1 -> 2) and full key set rewritten
        capturedMetadata.Should().NotBeNull();
        capturedMetadata!.Should().HaveCount(5);
        capturedMetadata[MetaState].Should().Be(StateOrphaned);
        capturedMetadata[MetaOrphanReason].Should().Be(ReasonNoJob);
        capturedMetadata[MetaOrphanAttempts].Should().Be("1", "the attempt count must increment from 0 to 1");

        // A distinct error EventId should be logged for the empty-IngestionJobId case (D6)
        var errorEventIds = CapturedErrorEventIds(loggerMock);
        errorEventIds.Should().ContainSingle(
            "the empty-IngestionJobId anchor-unsatisfiable branch must emit exactly one Error-level log record (D6)");
        errorEventIds[0].Id.Should().NotBe(0,
            "the EventId must be a stable, non-default value for monitoring");

        _blobStorageMock.Verify(
            x => x.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Result counts: the blob was found and processed as job-absent (increment path)
        result.OrphansFound.Should().Be(1);
        result.Healed.Should().Be(0, "empty IngestionJobId must never result in healing");
        result.AttemptsIncremented.Should().Be(1, "the blob should be processed as job-absent");
        result.TerminalTransitions.Should().Be(0, "within budget and window, so plain increment");
        result.Errored.Should().Be(0);
    }

    // --- (b) Increment: job still absent -> orphanAttempts +1, full key set rewritten, firstDetected preserved ---

    [Fact]
    public async Task RunSweepAsync_JobStillAbsentAndBelowBudgetAndWithinWindow_IncrementsAttemptsAndRewritesFullKeySetPreservingFirstDetected()
    {
        var uploadId = Guid.NewGuid().ToString();
        var originalFirstDetected = DateTimeOffset.UtcNow.AddHours(-2);
        var blob = OrphanBlob(uploadId, attempts: 1, firstDetectedUtc: originalFirstDetected, lastProcessedUtc: DateTimeOffset.UtcNow.AddHours(-2));

        _blobStorageMock.Setup(x => x.ListAsync(Container, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { blob });
        SetUpNoJobFound(uploadId);

        Dictionary<string, string>? capturedMetadata = null;
        _blobStorageMock
            .Setup(x => x.SetMetadataAsync(Container, blob.Name, It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, string>, CancellationToken>((_, _, metadata, _) => capturedMetadata = metadata)
            .Returns(Task.CompletedTask);

        var result = await _sut.RunSweepAsync();

        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no job was found -- fail-closed (D1) means the coordinator is never invoked without a real job anchor");

        _blobStorageMock.Verify(
            x => x.SetMetadataAsync(Container, blob.Name, It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        capturedMetadata.Should().NotBeNull();
        capturedMetadata.Should().HaveCount(5,
            "SetMetadataAsync replaces the ENTIRE metadata collection -- a partial update would silently drop keys");
        capturedMetadata![MetaState].Should().Be(StateOrphaned);
        capturedMetadata[MetaOrphanReason].Should().Be(ReasonNoJob);
        capturedMetadata[MetaOrphanAttempts].Should().Be("2", "the attempt count must increment by exactly 1");
        capturedMetadata[MetaOrphanFirstDetectedUtc].Should().Be(
            originalFirstDetected.UtcDateTime.ToString("O"),
            "the original first-detected timestamp must be preserved, never reset to now");
        capturedMetadata[MetaDateLastProcessed].Should().NotBeNullOrWhiteSpace();

        _blobStorageMock.Verify(
            x => x.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.OrphansFound.Should().Be(1);
        result.Healed.Should().Be(0);
        result.AttemptsIncremented.Should().Be(1);
        result.TerminalTransitions.Should().Be(0);
        result.Errored.Should().Be(0);
    }

    // --- (c) Terminal by attempts: attempts reach MaxOrphanRetryAttempts -> OrphanedTerminal + NoIngestionJobAfterMaxAttempts ---

    [Fact]
    public async Task RunSweepAsync_JobStillAbsentAndAttemptsReachMax_TransitionsTerminalWithMaxAttemptsReasonAndLogsStableEventId()
    {
        var uploadId = Guid.NewGuid().ToString();
        var originalFirstDetected = DateTimeOffset.UtcNow.AddHours(-1);
        // Existing attempts=4; this failed resolution makes it the 5th attempt, meeting MaxOrphanRetryAttempts=5.
        var blob = OrphanBlob(uploadId, attempts: 4, firstDetectedUtc: originalFirstDetected);

        _blobStorageMock.Setup(x => x.ListAsync(Container, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { blob });
        SetUpNoJobFound(uploadId);

        Dictionary<string, string>? capturedMetadata = null;
        _blobStorageMock
            .Setup(x => x.SetMetadataAsync(Container, blob.Name, It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, string>, CancellationToken>((_, _, metadata, _) => capturedMetadata = metadata)
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<OrphanedArtifactSweepService>>();
        var sut = CreateSutWithLogger(loggerMock.Object);

        var result = await sut.RunSweepAsync();

        capturedMetadata.Should().NotBeNull();
        capturedMetadata.Should().HaveCount(5);
        capturedMetadata![MetaState].Should().Be(StateOrphanedTerminal);
        capturedMetadata[MetaOrphanReason].Should().Be(ReasonMaxAttempts);
        capturedMetadata[MetaOrphanAttempts].Should().Be("5");
        capturedMetadata[MetaOrphanFirstDetectedUtc].Should().Be(originalFirstDetected.UtcDateTime.ToString("O"));

        var errorInvocations = loggerMock.Invocations
            .Where(i => i.Method.Name == nameof(ILogger.Log)
                && i.Arguments[0] is LogLevel level && level == LogLevel.Error)
            .Select(i => (EventId)i.Arguments[1]!)
            .ToList();
        errorInvocations.Should().ContainSingle(e => e.Id == 1003,
            "a stable, distinct EventId (continuing the ProcessorArtifactService 100x sequence past 1001/1002) " +
            "must bind the max-attempts terminal transition so an Azure Monitor alert rule can attach to it");

        _blobStorageMock.Verify(
            x => x.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.OrphansFound.Should().Be(1);
        result.TerminalTransitions.Should().Be(1);
        result.AttemptsIncremented.Should().Be(0);
        result.Healed.Should().Be(0);
        result.Errored.Should().Be(0);
    }

    // --- (d) Terminal by window: firstDetected older than OrphanRetentionWindow -> terminal even below attempt budget ---

    [Fact]
    public async Task RunSweepAsync_JobStillAbsentAndFirstDetectedOlderThanRetentionWindow_TransitionsTerminalWithWindowReasonEvenBelowAttemptBudget()
    {
        var uploadId = Guid.NewGuid().ToString();
        // Well past the 24h retention window, but attempts is only 1 -- far below MaxOrphanRetryAttempts=5.
        var originalFirstDetected = DateTimeOffset.UtcNow.AddHours(-25);
        var blob = OrphanBlob(uploadId, attempts: 1, firstDetectedUtc: originalFirstDetected);

        _blobStorageMock.Setup(x => x.ListAsync(Container, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { blob });
        SetUpNoJobFound(uploadId);

        Dictionary<string, string>? capturedMetadata = null;
        _blobStorageMock
            .Setup(x => x.SetMetadataAsync(Container, blob.Name, It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, string>, CancellationToken>((_, _, metadata, _) => capturedMetadata = metadata)
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<OrphanedArtifactSweepService>>();
        var sut = CreateSutWithLogger(loggerMock.Object);

        var result = await sut.RunSweepAsync();

        capturedMetadata.Should().NotBeNull();
        capturedMetadata.Should().HaveCount(5);
        capturedMetadata![MetaState].Should().Be(StateOrphanedTerminal);
        capturedMetadata[MetaOrphanReason].Should().Be(ReasonRetentionWindow,
            "the window check must fire independently of the attempt-count check, even though attempts is nowhere near the budget");
        capturedMetadata[MetaOrphanFirstDetectedUtc].Should().Be(originalFirstDetected.UtcDateTime.ToString("O"));

        var errorInvocations = loggerMock.Invocations
            .Where(i => i.Method.Name == nameof(ILogger.Log)
                && i.Arguments[0] is LogLevel level && level == LogLevel.Error)
            .Select(i => (EventId)i.Arguments[1]!)
            .ToList();
        errorInvocations.Should().ContainSingle(e => e.Id == 1004,
            "the retention-window terminal transition must bind its own distinct stable EventId, different from " +
            "the max-attempts EventId (1003)");

        _blobStorageMock.Verify(
            x => x.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.OrphansFound.Should().Be(1);
        result.TerminalTransitions.Should().Be(1);
        result.AttemptsIncremented.Should().Be(0);
        result.Healed.Should().Be(0);
        result.Errored.Should().Be(0);
    }

    // --- (e) Terminal blobs are skipped entirely: no job resolution, no metadata write ---

    [Fact]
    public async Task RunSweepAsync_BlobAlreadyOrphanedTerminal_IsSkippedEntirely_NoJobResolutionNoMetadataWrite()
    {
        var uploadId = Guid.NewGuid().ToString();
        var blob = TerminalBlob(uploadId, reason: ReasonMaxAttempts, attempts: 5, firstDetectedUtc: DateTimeOffset.UtcNow.AddDays(-3));

        _blobStorageMock.Setup(x => x.ListAsync(Container, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { blob });

        var result = await _sut.RunSweepAsync();

        _jobRepoMock.Verify(
            x => x.GetLatestByInputAsync(It.IsAny<string>(), It.IsAny<IngestionJobType>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "D8: an already-terminal blob must not even trigger a job-resolution attempt");
        _blobStorageMock.Verify(
            x => x.SetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _blobStorageMock.Verify(
            x => x.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.OrphansFound.Should().Be(0, "a terminal blob is not an active orphan being processed this cycle");
        result.Healed.Should().Be(0);
        result.AttemptsIncremented.Should().Be(0);
        result.TerminalTransitions.Should().Be(0);
        result.Errored.Should().Be(0);
    }

    // --- (f) Non-orphan blobs ignored entirely ---

    [Theory]
    [InlineData("Completed")]
    [InlineData("PartiallyIndexed")]
    [InlineData("Failed")]
    [InlineData("Pending")]
    public async Task RunSweepAsync_BlobStateIsNotOrphanOrTerminal_IsIgnoredEntirely(string state)
    {
        var uploadId = Guid.NewGuid().ToString();
        var blob = NonOrphanBlob(uploadId, state);

        _blobStorageMock.Setup(x => x.ListAsync(Container, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { blob });

        var result = await _sut.RunSweepAsync();

        _jobRepoMock.Verify(
            x => x.GetLatestByInputAsync(It.IsAny<string>(), It.IsAny<IngestionJobType>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _blobStorageMock.Verify(
            x => x.SetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _blobStorageMock.Verify(
            x => x.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.OrphansFound.Should().Be(0);
        result.Healed.Should().Be(0);
        result.AttemptsIncremented.Should().Be(0);
        result.TerminalTransitions.Should().Be(0);
        result.Errored.Should().Be(0);
    }

    [Fact]
    public async Task RunSweepAsync_BlobHasNoStateMetadataKeyAtAll_IsIgnoredEntirely()
    {
        var uploadId = Guid.NewGuid().ToString();
        var blob = NonOrphanBlob(uploadId, state: null);

        _blobStorageMock.Setup(x => x.ListAsync(Container, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { blob });

        var result = await _sut.RunSweepAsync();

        _jobRepoMock.Verify(
            x => x.GetLatestByInputAsync(It.IsAny<string>(), It.IsAny<IngestionJobType>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _blobStorageMock.Verify(
            x => x.SetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.OrphansFound.Should().Be(0);
    }

    // --- (g) Continue on error: one blob throwing does not abort the cycle ---

    [Fact]
    public async Task RunSweepAsync_OneBlobThrowsDuringJobResolution_ContinuesToNextBlobAndCountsError()
    {
        var failingUploadId = Guid.NewGuid().ToString();
        var failingBlob = OrphanBlob(failingUploadId, attempts: 0, firstDetectedUtc: DateTimeOffset.UtcNow.AddMinutes(-30));

        var healingUploadId = Guid.NewGuid().ToString();
        var healingJob = IngestionJob.Create(IngestionJobType.PDFManual, healingUploadId, createdBySubject: null);
        var healingBlob = OrphanBlob(healingUploadId, attempts: 1, firstDetectedUtc: DateTimeOffset.UtcNow.AddHours(-1));

        _blobStorageMock
            .Setup(x => x.ListAsync(Container, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { failingBlob, healingBlob });

        _jobRepoMock
            .Setup(x => x.GetLatestByInputAsync(failingUploadId, IngestionJobType.PDFManual, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("job repository unavailable"));

        SetUpJobFound(healingUploadId, healingJob);
        _blobStorageMock
            .Setup(x => x.DownloadAsync(Container, healingBlob.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(new byte[] { 1 }));
        _coordinatorMock
            .Setup(x => x.IndexAsync(It.IsAny<Stream>(), healingUploadId, Container, healingBlob.Name, It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string _, string _, string _, Guid artifactId, IngestionJob resolvedJob, CancellationToken _) =>
                SucceededOutcome(artifactId, resolvedJob.IngestionJobId));

        OrphanSweepResultDto result = null!;
        var act = async () => result = await _sut.RunSweepAsync();

        await act.Should().NotThrowAsync(
            "a single blob's processing failure must not abort the whole sweep cycle");

        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), healingUploadId, Container, healingBlob.Name, It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "processing must continue to the next blob after the failing one");

        _blobStorageMock.Verify(
            x => x.SetMetadataAsync(Container, failingBlob.Name, It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the failing blob threw before any metadata could be computed -- no partial/incorrect write");

        _blobStorageMock.Verify(
            x => x.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.Should().NotBeNull();
        result.OrphansFound.Should().Be(2);
        result.Healed.Should().Be(1);
        result.Errored.Should().Be(1,
            "the errored blob's failure must be reflected in the returned counts, not silently dropped");
    }

    [Fact]
    public async Task RunSweepAsync_OneBlobThrowsDuringMetadataWrite_ContinuesToNextBlobAndCountsError()
    {
        var failingUploadId = Guid.NewGuid().ToString();
        var failingBlob = OrphanBlob(failingUploadId, attempts: 1, firstDetectedUtc: DateTimeOffset.UtcNow.AddHours(-1));

        var healingUploadId = Guid.NewGuid().ToString();
        var healingJob = IngestionJob.Create(IngestionJobType.PDFManual, healingUploadId, createdBySubject: null);
        var healingBlob = OrphanBlob(healingUploadId, attempts: 0, firstDetectedUtc: DateTimeOffset.UtcNow.AddMinutes(-10));

        _blobStorageMock
            .Setup(x => x.ListAsync(Container, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { failingBlob, healingBlob });

        SetUpNoJobFound(failingUploadId);
        _blobStorageMock
            .Setup(x => x.SetMetadataAsync(Container, failingBlob.Name, It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("blob store unavailable"));

        SetUpJobFound(healingUploadId, healingJob);
        _blobStorageMock
            .Setup(x => x.DownloadAsync(Container, healingBlob.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(new byte[] { 1 }));
        _coordinatorMock
            .Setup(x => x.IndexAsync(It.IsAny<Stream>(), healingUploadId, Container, healingBlob.Name, It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string _, string _, string _, Guid artifactId, IngestionJob resolvedJob, CancellationToken _) =>
                SucceededOutcome(artifactId, resolvedJob.IngestionJobId));

        OrphanSweepResultDto result = null!;
        var act = async () => result = await _sut.RunSweepAsync();

        await act.Should().NotThrowAsync();

        _coordinatorMock.Verify(
            x => x.IndexAsync(It.IsAny<Stream>(), healingUploadId, Container, healingBlob.Name, It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()),
            Times.Once);

        result.Should().NotBeNull();
        result.OrphansFound.Should().Be(2);
        result.Healed.Should().Be(1);
        result.Errored.Should().Be(1);
    }

    // --- (h) Never deleted: consolidated check across every branch in a single sweep cycle ---

    [Fact]
    public async Task RunSweepAsync_AcrossEveryBranchInOneCycle_NeverCallsDeleteOnBlobStorage()
    {
        var healUploadId = Guid.NewGuid().ToString();
        var healJob = IngestionJob.Create(IngestionJobType.PDFManual, healUploadId, createdBySubject: null);
        var healBlob = OrphanBlob(healUploadId, attempts: 0, firstDetectedUtc: DateTimeOffset.UtcNow.AddMinutes(-5));

        var incrementUploadId = Guid.NewGuid().ToString();
        var incrementBlob = OrphanBlob(incrementUploadId, attempts: 1, firstDetectedUtc: DateTimeOffset.UtcNow.AddHours(-1));

        var terminalByAttemptsUploadId = Guid.NewGuid().ToString();
        var terminalByAttemptsBlob = OrphanBlob(terminalByAttemptsUploadId, attempts: 4, firstDetectedUtc: DateTimeOffset.UtcNow.AddHours(-2));

        var terminalByWindowUploadId = Guid.NewGuid().ToString();
        var terminalByWindowBlob = OrphanBlob(terminalByWindowUploadId, attempts: 1, firstDetectedUtc: DateTimeOffset.UtcNow.AddHours(-30));

        var alreadyTerminalUploadId = Guid.NewGuid().ToString();
        var alreadyTerminalBlob = TerminalBlob(alreadyTerminalUploadId, ReasonMaxAttempts, attempts: 5, firstDetectedUtc: DateTimeOffset.UtcNow.AddDays(-5));

        var nonOrphanUploadId = Guid.NewGuid().ToString();
        var nonOrphanBlob = NonOrphanBlob(nonOrphanUploadId, "Completed");

        var erroringUploadId = Guid.NewGuid().ToString();
        var erroringBlob = OrphanBlob(erroringUploadId, attempts: 0, firstDetectedUtc: DateTimeOffset.UtcNow.AddMinutes(-1));

        _blobStorageMock
            .Setup(x => x.ListAsync(Container, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                healBlob, incrementBlob, terminalByAttemptsBlob, terminalByWindowBlob,
                alreadyTerminalBlob, nonOrphanBlob, erroringBlob
            });

        SetUpJobFound(healUploadId, healJob);
        _blobStorageMock.Setup(x => x.DownloadAsync(Container, healBlob.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(new byte[] { 1 }));
        _coordinatorMock
            .Setup(x => x.IndexAsync(It.IsAny<Stream>(), healUploadId, Container, healBlob.Name, It.IsAny<Guid>(), It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string _, string _, string _, Guid artifactId, IngestionJob resolvedJob, CancellationToken _) =>
                SucceededOutcome(artifactId, resolvedJob.IngestionJobId));

        SetUpNoJobFound(incrementUploadId);
        SetUpNoJobFound(terminalByAttemptsUploadId);
        SetUpNoJobFound(terminalByWindowUploadId);

        _jobRepoMock
            .Setup(x => x.GetLatestByInputAsync(erroringUploadId, IngestionJobType.PDFManual, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated failure"));

        _blobStorageMock
            .Setup(x => x.SetMetadataAsync(Container, It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.RunSweepAsync();

        _blobStorageMock.Verify(
            x => x.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "D10: terminal orphans (and every other branch) are never auto-deleted -- the blob is the only " +
            "surviving copy of paid-for chunking work");

        result.OrphansFound.Should().Be(5, "heal + increment + terminal-by-attempts + terminal-by-window + errored, excluding the already-terminal and non-orphan blobs");
        result.Healed.Should().Be(1);
        result.AttemptsIncremented.Should().Be(1);
        result.TerminalTransitions.Should().Be(2);
        result.Errored.Should().Be(1);
    }
}
