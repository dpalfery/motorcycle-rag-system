using Azure;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.Graph;
using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using System.Reflection;

namespace MotorcycleRAG.UnitTests.Pipeline;

/// <summary>
/// Additional unit tests for <see cref="IngestionJobService"/> paths not covered by the main test suites.
/// </summary>
public sealed class IngestionJobServiceCoverageTests {
    private const string TestUserId = "test-user-oid";

    private readonly Mock<IIngestionJobRepository> _repository;
    private readonly Mock<IBlobStorageService> _blobStorage;
    private readonly Mock<IIndexedArtifactRepository> _artifactRepository;
    private readonly Mock<IIndexedChunkRepository> _chunkRepository;
    private readonly Mock<IAzureSearchDocumentService> _searchDocumentService;
    private readonly Mock<IGraphRepository> _graphRepository;
    private readonly Mock<IGraphEntityIngestionService> _graphEntityIngestionService;
    private readonly Mock<IBikeModelRepository> _bikeModelRepository;
    private readonly Mock<IManualDocumentRepository> _manualDocumentRepository;
    private readonly ManualBikeLinker _manualBikeLinker;
    private readonly GraphIngestionChannel _graphIngestionChannel;
    private readonly Mock<ILogger<IngestionJobService>> _logger;

    public IngestionJobServiceCoverageTests() {
        _repository = new Mock<IIngestionJobRepository>();
        _blobStorage = new Mock<IBlobStorageService>();
        _artifactRepository = new Mock<IIndexedArtifactRepository>();
        _chunkRepository = new Mock<IIndexedChunkRepository>();
        _searchDocumentService = new Mock<IAzureSearchDocumentService>();
        _graphRepository = new Mock<IGraphRepository>();
        _graphEntityIngestionService = new Mock<IGraphEntityIngestionService>();
        _bikeModelRepository = new Mock<IBikeModelRepository>();
        _manualDocumentRepository = new Mock<IManualDocumentRepository>();
        _graphIngestionChannel = new GraphIngestionChannel();
        _logger = new Mock<ILogger<IngestionJobService>>();

        // Create ManualBikeLinker with real instance (it's sealed, can't be mocked)
        var linkerLogger = new Mock<ILogger<ManualBikeLinker>>();
        _manualBikeLinker = new ManualBikeLinker(_bikeModelRepository.Object, _graphRepository.Object, linkerLogger.Object);

        _repository.Setup(r => r.CreateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>())).ReturnsAsync((IngestionJob job, CancellationToken _) => job);
        _repository.Setup(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _repository.Setup(r => r.UpdateStageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _repository.Setup(r => r.UpdateMetadataAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _repository.Setup(r => r.TrySetDeletingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repository.Setup(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repository.Setup(r => r.DeleteByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _repository.Setup(r => r.DeleteByStatusesAsync(It.IsAny<IReadOnlyCollection<IngestionJobStatus>>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        _artifactRepository.Setup(r => r.GetByIngestionJobIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IndexedArtifactDto>());
        _artifactRepository.Setup(r => r.GetByUploadIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IndexedArtifactDto>());
        _artifactRepository.Setup(r => r.DeleteByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        _chunkRepository.Setup(r => r.GetByArtifactIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IndexedChunkDto>());
        _chunkRepository.Setup(r => r.GetByIngestionJobIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IndexedChunkDto>());
        _chunkRepository.Setup(r => r.GetByUploadIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IndexedChunkDto>());
        _chunkRepository.Setup(r => r.DeleteByArtifactIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _chunkRepository.Setup(r => r.DeleteByIngestionJobIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _chunkRepository.Setup(r => r.DeleteByUploadIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _blobStorage.Setup(s => s.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _blobStorage.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _searchDocumentService.Setup(s => s.DeleteDocumentsAsync(It.IsAny<IEnumerable<string>>())).Returns(Task.CompletedTask);
        _graphRepository.Setup(r => r.DeleteByDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _graphEntityIngestionService.Setup(g => g.IngestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private IngestionJobService CreateSut(ILogger<IngestionJobService>? logger = null) => new(
        _repository.Object,
        _blobStorage.Object,
        _artifactRepository.Object,
        _chunkRepository.Object,
        _searchDocumentService.Object,
        _graphRepository.Object,
        _graphEntityIngestionService.Object,
        _graphIngestionChannel,
        _manualBikeLinker,
        Options.Create(new BlobStorageOptions { RawUploadsContainer = "raw-uploads" }),
        Options.Create(new IngestionOptions { MaxInputBytes = 2_000_000_000L }),
        logger ?? _logger.Object);

    private static IngestionJob MakeJob(
        Guid ingestionJobId,
        IngestionJobStatus status,
        IngestionJobType inputType = IngestionJobType.PDFManual,
        string inputRef = "upload-test",
        string? missingPagesJson = null,
        int? expectedChunkCount = null,
        string? currentStage = null,
        string? computeProvider = null,
        string? metadataJson = null,
        string? sourceFileName = null) =>
        IngestionJob.Rehydrate(
            id: 1,
            ingestionJobId: ingestionJobId,
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: null,
            completedAtUtc: null,
            createdBySubject: null,
            status: status,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: inputType,
            inputRef: inputRef,
            sourceFileName: sourceFileName,
            computeProvider: computeProvider ?? "MicrosoftFabric",
            docIngestionRunId: null,
            manualDocumentId: null,
            totalPages: null,
            pagesCapturedViewableCount: null,
            pagesWithSearchableTextCount: null,
            pagesWithOcrTextCount: null,
            pagesWithNativeTextCount: null,
            missingPagesJson: missingPagesJson,
            metricsJson: null,
            expectedChunkCount: expectedChunkCount,
            indexedChunkCount: null,
            currentStage: currentStage,
            stageSetAtUtc: null,
            metadataJson: metadataJson);

    [Fact]
    public async Task GetRecentIngestionJobsAsync_MaxCountLessThanOrEqualZero_ThrowsArgumentOutOfRangeException() {
        var sut = CreateSut();
        var act = () => sut.GetRecentIngestionJobsAsync(0, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("maxCount");
    }

    [Fact]
    public async Task GetRecentIngestionJobsAsync_MapsMissingPagesJson() {
        var job = IngestionJob.Rehydrate(
            id: 1,
            ingestionJobId: Guid.NewGuid(),
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: null,
            completedAtUtc: null,
            createdBySubject: null,
            status: IngestionJobStatus.Completed,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: IngestionJobType.PDFManual,
            inputRef: "upload-missing-pages",
            sourceFileName: null,
            computeProvider: "MicrosoftFabric",
            docIngestionRunId: null,
            manualDocumentId: null,
            totalPages: null,
            pagesCapturedViewableCount: null,
            pagesWithSearchableTextCount: null,
            pagesWithOcrTextCount: null,
            pagesWithNativeTextCount: null,
            missingPagesJson: "[1,2,3]",
            metricsJson: null,
            expectedChunkCount: null,
            indexedChunkCount: null,
            currentStage: null,
            stageSetAtUtc: null,
            metadataJson: null);
        _repository.Setup(r => r.GetRecentAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync([job]);
        var sut = CreateSut();

        var result = await sut.GetRecentIngestionJobsAsync(50, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].MissingPages.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task GetRecentIngestionJobsAsync_MalformedMissingPagesJson_ReturnsEmpty() {
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Completed, missingPagesJson: "not valid json");
        _repository.Setup(r => r.GetRecentAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync([job]);
        var sut = CreateSut();

        var result = await sut.GetRecentIngestionJobsAsync(50, CancellationToken.None);

        result[0].MissingPages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetJobStatusAsync_WhenJobNotFound_ReturnsNull() {
        var jobId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync((IngestionJob?)null);
        var sut = CreateSut();

        var result = await sut.GetJobStatusAsync(jobId, TestUserId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ClearFailedJobsAsync_DelegatesToRepositoryAndReturnsCount() {
        _repository.Setup(r => r.DeleteByStatusesAsync(
            It.Is<IReadOnlyCollection<IngestionJobStatus>>(s => s.Contains(IngestionJobStatus.Failed) && s.Contains(IngestionJobStatus.Cancelled)),
            It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var sut = CreateSut();

        var result = await sut.ClearFailedJobsAsync(TestUserId, CancellationToken.None);

        result.Should().Be(3);
    }

    [Fact]
    public async Task ClearFinishedJobsAsync_WhenNoFinishedJobs_ReturnsZero() {
        _repository.Setup(r => r.GetByStatusesAsync(
            It.Is<IReadOnlyCollection<IngestionJobStatus>>(s => s.Contains(IngestionJobStatus.Completed) && s.Contains(IngestionJobStatus.PartiallyCompleted)),
            It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IngestionJob>());
        var sut = CreateSut();

        var result = await sut.ClearFinishedJobsAsync(TestUserId, CancellationToken.None);

        result.Should().Be(0);
        _repository.Verify(r => r.DeleteByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelJobAsync_NonTerminalJob_CancelsAndUpdates() {
        var job = IngestionJob.Create(IngestionJobType.PDFManual, "upload-cancel", createdBySubject: null,
            initialStatus: IngestionJobStatus.Processing);
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        await sut.CancelJobAsync(jobId, TestUserId, CancellationToken.None);

        job.Status.Should().Be(IngestionJobStatus.Cancelled);
        _repository.Verify(r => r.UpdateAsync(job, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelJobAsync_JobNotFound_ThrowsInvalidOperationException() {
        var jobId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync((IngestionJob?)null);
        var sut = CreateSut();

        var act = () => sut.CancelJobAsync(jobId, TestUserId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*'{jobId}' not found*");
    }

    [Fact]
    public async Task FailJobAsync_ProcessingJob_PersistsFailedStateAndStage() {
        var job = IngestionJob.Create(IngestionJobType.PDFManual, "upload-fail", createdBySubject: null,
            initialStatus: IngestionJobStatus.Processing);
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        await sut.FailJobAsync(jobId, "processor error", TestUserId, CancellationToken.None);

        job.Status.Should().Be(IngestionJobStatus.Failed);
        job.CurrentStage.Should().Be("failed");
        _repository.Verify(r => r.UpdateAsync(job, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task FailJobAsync_ReasonContainsControlCharacters_LogsSanitizedStructuredField() {
        // The service now sanitizes the reason inline via LogSanitizer.Sanitize before logging
        // (CWE-117 log-forging defense-in-depth alongside the runtime SanitizingLoggerProvider),
        // so the spy CapturingLogger observes the escaped value, not the raw one.
        const string attackerReason = "processor\\name\r\nforged\tentry";
        var job = IngestionJob.Create(
            IngestionJobType.PDFManual,
            "upload-fail-log-encoding",
            createdBySubject: null,
            initialStatus: IngestionJobStatus.Processing);
        var logger = new CapturingLogger<IngestionJobService>();
        _repository.Setup(r => r.GetByIdAsync(job.IngestionJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut(logger);

        await sut.FailJobAsync(job.IngestionJobId, attackerReason, TestUserId, CancellationToken.None);

        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.LogLevel.Should().Be(LogLevel.Information);
        entry.Properties.Should().ContainKeys("JobId", "Reason", "{OriginalFormat}");
        entry.Properties["{OriginalFormat}"].Should().Be("Ingestion job {JobId} marked as failed: {Reason}");
        entry.Properties["Reason"].Should().Be(LogSanitizer.Sanitize(attackerReason));
    }

    private sealed class CapturingLogger<T> : ILogger<T> {
        public List<CapturedLogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) {
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

            if (state is IReadOnlyList<KeyValuePair<string, object?>> stateList) {
                foreach (var pair in stateList) {
                    properties[pair.Key] = pair.Value;
                }
            }

            Entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), properties));
        }

        private sealed class NoopScope : IDisposable {
            public static readonly NoopScope Instance = new();

            public void Dispose() {
            }
        }
    }

    private sealed record CapturedLogEntry(
        LogLevel LogLevel,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);

    [Fact]
    public async Task FailJobAsync_JobNotFound_ThrowsInvalidOperationException() {
        var jobId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync((IngestionJob?)null);
        var sut = CreateSut();

        var act = () => sut.FailJobAsync(jobId, "error", TestUserId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*'{jobId}' not found*");
    }

    [Fact]
    public async Task ImportGraphArtifactsAsync_WhenBlobExists_EnqueuesAndReturnsProcessingJob() {
        const string uploadId = "upload-graph-001";
        _blobStorage.Setup(b => b.ExistsAsync("raw-uploads", $"graph-entities/{uploadId}/entities.json", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var sut = CreateSut();

        var result = await sut.ImportGraphArtifactsAsync(new GraphImportStartRequest { UploadId = uploadId }, TestUserId, CancellationToken.None);

        result.Status.Should().Be(IngestionJobStatus.Processing.ToString());
        _graphEntityIngestionService.Verify(g => g.IngestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessGraphIngestionJobAsync_CompletesJobWhenSuccessful() {
        var job = IngestionJob.Create(IngestionJobType.BikeGraph, "upload-graph-002", createdBySubject: null,
            initialStatus: IngestionJobStatus.Processing);
        var sut = CreateSut();

        await sut.ProcessGraphIngestionJobAsync(job);

        job.Status.Should().Be(IngestionJobStatus.Completed);
        _graphEntityIngestionService.Verify(g => g.IngestAsync("upload-graph-002", CancellationToken.None), Times.Once);
        _repository.Verify(r => r.UpdateAsync(job, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ProcessGraphIngestionJobAsync_WhenIngestionFails_FailsJobAndRecordsFailure() {
        var job = IngestionJob.Create(IngestionJobType.BikeGraph, "upload-graph-003", createdBySubject: null,
            initialStatus: IngestionJobStatus.Processing);
        _graphEntityIngestionService.Setup(g => g.IngestAsync("upload-graph-003", CancellationToken.None)).ThrowsAsync(new InvalidOperationException("graph failed"));
        var sut = CreateSut();

        await sut.ProcessGraphIngestionJobAsync(job);

        job.Status.Should().Be(IngestionJobStatus.Failed);
        _repository.Verify(r => r.UpdateAsync(job, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ProcessGraphIngestionJobAsync_NullJob_ThrowsArgumentNullException() {
        var sut = CreateSut();
        var act = () => sut.ProcessGraphIngestionJobAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("job");
    }

    // --- T11 (plan 6-Docs/plans/2026-08-01-vector-graph-anchor-id-contract.md, §4/§5/§7):
    // ManualBikeLinker.LinkAsync must be invoked by an actual RunGraphIngestionAsync pipeline
    // run, not asserted via a direct call to the linker (that would prove nothing about wiring).
    //
    // Mockability formulation chosen: IngestionJobService's current constructor (11 params) does
    // not accept anything ManualBikeLinker-compatible today — wiring it in is T11's own
    // implementation work, which this task must not perform, and ManualBikeLinker is a concrete
    // sealed class with no interface, so it cannot be mocked directly without introducing
    // IManualBikeLinker (also out of scope for this task).
    //
    // Preferred formulation from the task brief: assert against the collaborator the linker
    // itself depends on — IGraphRepository.UpsertEdgeAsync — using a REAL ManualBikeLinker
    // instance (built from a mocked IBikeModelRepository and this fixture's shared
    // _graphRepository mock) so no mock of the linker is ever required. The only remaining gap is
    // constructing the SUT with that real linker wired in: since referencing a constructor
    // parameter that does not exist yet would fail to compile — and because this project is
    // compiled as a single unit, that would break every other test in it, including other agents'
    // concurrent work — TryCreateSutWithLinker below discovers the (not-yet-existing) constructor
    // via reflection at test-run time instead of at compile time. Today it finds no matching
    // constructor and the test fails cleanly on that assertion (RED, for the right reason: the
    // pipeline is not wired to the linker at all). Once T11 adds a constructor parameter whose
    // type is ManualBikeLinker itself, or an interface ManualBikeLinker implements (e.g. a future
    // IManualBikeLinker), this resolves it automatically and the rest of the test exercises the
    // real pipeline call path.
    //
    // What the implementer must provide for this to go GREEN:
    //   1. Add a constructor parameter to IngestionJobService whose type is ManualBikeLinker (or
    //      an interface it implements) and store it as a field.
    //   2. In RunGraphIngestionAsync, after _graphEntityIngestionService.IngestAsync(...)
    //      succeeds, when job.ManualDocumentId.HasValue and TryParseMetadata(job.MetadataJson)
    //      yields non-null Make/Model/Year, call linker.LinkAsync(job.ManualDocumentId.Value,
    //      make, model, year.Value, ct) — never throwing on missing/unparseable metadata (D5).

    /// <summary>
    /// Locates (via reflection, not a compile-time reference) an <see cref="IngestionJobService"/>
    /// constructor that accepts a parameter assignable from <see cref="ManualBikeLinker"/> — either
    /// the concrete class or an interface it implements — and invokes it with this fixture's
    /// existing mocked collaborators plus <paramref name="linker"/>. Returns a null Sut and a
    /// diagnostic reason when no such constructor exists yet, which is the current (pre-T11) state
    /// of the production code.
    /// </summary>
    private (IngestionJobService? Sut, string? FailureReason) TryCreateSutWithLinker(ManualBikeLinker linker) {
        var availableServices = new Dictionary<Type, object> {
            [typeof(IIngestionJobRepository)] = _repository.Object,
            [typeof(IBlobStorageService)] = _blobStorage.Object,
            [typeof(IIndexedArtifactRepository)] = _artifactRepository.Object,
            [typeof(IIndexedChunkRepository)] = _chunkRepository.Object,
            [typeof(IAzureSearchDocumentService)] = _searchDocumentService.Object,
            [typeof(IGraphRepository)] = _graphRepository.Object,
            [typeof(IGraphEntityIngestionService)] = _graphEntityIngestionService.Object,
            [typeof(GraphIngestionChannel)] = _graphIngestionChannel,
            [typeof(IOptions<BlobStorageOptions>)] = Options.Create(new BlobStorageOptions { RawUploadsContainer = "raw-uploads" }),
            [typeof(IOptions<IngestionOptions>)] = Options.Create(new IngestionOptions { MaxInputBytes = 2_000_000_000L }),
            [typeof(ILogger<IngestionJobService>)] = _logger.Object
        };

        var candidateCtor = typeof(IngestionJobService)
            .GetConstructors()
            .Where(ctor => ctor.GetParameters().Any(p => p.ParameterType.IsAssignableFrom(typeof(ManualBikeLinker))))
            .OrderByDescending(ctor => ctor.GetParameters().Length)
            .FirstOrDefault();

        if (candidateCtor is null) {
            return (null,
                "IngestionJobService currently has no constructor accepting a ManualBikeLinker-compatible " +
                "parameter — T11 (wiring ManualBikeLinker into the pipeline) has not been implemented yet.");
        }

        var parameters = candidateCtor.GetParameters();
        var args = new object[parameters.Length];
        for (var i = 0; i < parameters.Length; i++) {
            var paramType = parameters[i].ParameterType;
            if (paramType.IsAssignableFrom(typeof(ManualBikeLinker))) {
                args[i] = linker;
                continue;
            }

            var matchingType = availableServices.Keys.FirstOrDefault(t => paramType.IsAssignableFrom(t));
            if (matchingType is null) {
                return (null, $"No test double registered for constructor parameter type '{paramType.FullName}'.");
            }

            args[i] = availableServices[matchingType];
        }

        var instance = (IngestionJobService)candidateCtor.Invoke(args);
        return (instance, null);
    }

    [Fact]
    public async Task ProcessGraphIngestionJobAsync_ManualDocumentIdPresentWithParsedMetadata_LinksManualToBikeModelViaGraphRepository() {
        var manualDocumentId = Guid.NewGuid();
        var bikeModelId = Guid.NewGuid();
        var job = IngestionJob.Rehydrate(
            id: 1,
            ingestionJobId: Guid.NewGuid(),
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: null,
            completedAtUtc: null,
            createdBySubject: null,
            status: IngestionJobStatus.Processing,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: IngestionJobType.BikeGraph,
            inputRef: "upload-graph-linker-001",
            sourceFileName: null,
            computeProvider: "AdminLocalProcessor",
            docIngestionRunId: null,
            manualDocumentId: manualDocumentId,
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
            // Lower-case keys: matches IngestionJobService's private TryParseMetadata/ExtractMetadata
            // helper, which reads "make"/"model"/"year"/"category" via case-sensitive
            // JsonElement.TryGetProperty. "category" is included even though D5 only names
            // Make/Model/Year, so this test passes whether the implementer gates on those three
            // fields individually or on ParsedMetadata.IsComplete (all four).
            metadataJson: "{\"make\":\"Honda\",\"model\":\"CBR600RR\",\"year\":2024,\"category\":\"sport\"}");

        var bikeModel = BikeModel.Rehydrate(
            id: bikeModelId,
            make: "Honda",
            model: "CBR600RR",
            year: 2024,
            aliases: null,
            createdAtUtc: DateTimeOffset.UtcNow,
            updatedAtUtc: DateTimeOffset.UtcNow,
            createdByUserId: null,
            uploadRef: null);
        var bikeModelRepository = new Mock<IBikeModelRepository>();
        bikeModelRepository
            .Setup(r => r.FindCanonicalAsync("Honda", "CBR600RR", 2024, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bikeModel);
        var linker = new ManualBikeLinker(bikeModelRepository.Object, _graphRepository.Object, Mock.Of<ILogger<ManualBikeLinker>>());

        var (sut, failureReason) = TryCreateSutWithLinker(linker);
        sut.Should().NotBeNull(
            "IngestionJobService must gain a ManualBikeLinker-compatible constructor dependency once " +
            $"T11 wires it into RunGraphIngestionAsync (reflection diagnostic: {failureReason}).");

        await sut!.ProcessGraphIngestionJobAsync(job);

        // Asserted through the linker's own collaborator (IGraphRepository.UpsertEdgeAsync) rather
        // than a mock of LinkAsync itself — see the formulation note above this test group.
        _graphRepository.Verify(
            r => r.UpsertEdgeAsync(
                It.Is<GraphEdgeDto>(edge =>
                    edge.FromNodeId == manualDocumentId
                    && edge.ToNodeId == bikeModelId
                    && edge.RelationshipType == "manual-for-bike"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessGraphIngestionJobAsync_ManualDocumentIdNull_DoesNotLinkManualToBikeModelAndDoesNotThrow() {
        var job = IngestionJob.Create(
            IngestionJobType.BikeGraph,
            "upload-graph-linker-002",
            createdBySubject: null,
            initialStatus: IngestionJobStatus.Processing);
        // IngestionJob.Create never sets ManualDocumentId — it is null by construction, exercising
        // the guard path (D5: job.ManualDocumentId.HasValue).
        job.ManualDocumentId.Should().BeNull();
        job.SetMetadata("{\"make\":\"Honda\",\"model\":\"CBR600RR\",\"year\":2024,\"category\":\"sport\"}");

        var bikeModelRepository = new Mock<IBikeModelRepository>();
        var linker = new ManualBikeLinker(bikeModelRepository.Object, _graphRepository.Object, Mock.Of<ILogger<ManualBikeLinker>>());

        var (sut, failureReason) = TryCreateSutWithLinker(linker);
        sut.Should().NotBeNull(
            "IngestionJobService must gain a ManualBikeLinker-compatible constructor dependency once " +
            $"T11 wires it into RunGraphIngestionAsync (reflection diagnostic: {failureReason}).");

        var act = () => sut!.ProcessGraphIngestionJobAsync(job);

        await act.Should().NotThrowAsync();
        bikeModelRepository.Verify(
            r => r.FindCanonicalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _graphRepository.Verify(
            r => r.UpsertEdgeAsync(
                It.Is<GraphEdgeDto>(edge => edge.RelationshipType == "manual-for-bike"),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteJobCleanupAsync_RunBestEffortFailure_LogsWarningAndContinues() {
        var uploadId = Guid.NewGuid().ToString();
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Deleting, inputRef: uploadId, expectedChunkCount: 1);
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _searchDocumentService.Setup(s => s.DeleteDocumentsAsync(It.IsAny<IEnumerable<string>>())).ThrowsAsync(new InvalidOperationException("search down"));
        var sut = CreateSut();

        await sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        _repository.Verify(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteJobCleanupAsync_OverlappingArtifactsAndDuplicateChunkIds_DeletesEachDistinctAssetAndSearchDocument() {
        var uploadId = Guid.NewGuid().ToString();
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Deleting, inputRef: uploadId, expectedChunkCount: 1);
        var firstArtifactId = Guid.NewGuid();
        var secondArtifactId = Guid.NewGuid();
        var firstArtifact = new IndexedArtifactDto { IndexedArtifactId = firstArtifactId, UploadId = uploadId };
        var secondArtifact = new IndexedArtifactDto { IndexedArtifactId = secondArtifactId, UploadId = uploadId };
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _artifactRepository.Setup(r => r.GetByIngestionJobIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync([firstArtifact, secondArtifact]);
        _artifactRepository.Setup(r => r.GetByUploadIdAsync(uploadId, It.IsAny<CancellationToken>())).ReturnsAsync([firstArtifact]);
        _chunkRepository.Setup(r => r.GetByArtifactIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(firstArtifactId) && ids.Contains(secondArtifactId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new IndexedChunkDto { ChunkId = "artifact-chunk" },
                new IndexedChunkDto { ChunkId = "" }
            ]);
        _chunkRepository.Setup(r => r.GetByIngestionJobIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync([
            new IndexedChunkDto { ChunkId = "ARTIFACT-CHUNK" },
            new IndexedChunkDto { ChunkId = "job-chunk" }
        ]);
        _chunkRepository.Setup(r => r.GetByUploadIdAsync(uploadId, It.IsAny<CancellationToken>())).ReturnsAsync([
            new IndexedChunkDto { ChunkId = "   " },
            new IndexedChunkDto { ChunkId = "upload-chunk" }
        ]);
        var sut = CreateSut();

        await sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        _searchDocumentService.Verify(s => s.DeleteDocumentsAsync(It.Is<IEnumerable<string>>(ids =>
            ids.OrderBy(id => id).SequenceEqual(new[] {
                "artifact-chunk",
                "job-chunk",
                "upload-chunk",
                $"{uploadId}-pdf-0"
            }.OrderBy(id => id)))), Times.Once);
        _chunkRepository.Verify(r => r.DeleteByArtifactIdsAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(firstArtifactId) && ids.Contains(secondArtifactId)),
            It.IsAny<CancellationToken>()), Times.Once);
        _artifactRepository.Verify(r => r.DeleteByIdsAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(firstArtifactId) && ids.Contains(secondArtifactId)),
            It.IsAny<CancellationToken>()), Times.Once);
        _chunkRepository.Verify(r => r.DeleteByIngestionJobIdAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);
        _chunkRepository.Verify(r => r.DeleteByUploadIdAsync(uploadId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteJobCleanupAsync_WhenArtifactChunkDeletionFails_ContinuesWithRemainingCleanup() {
        var uploadId = Guid.NewGuid().ToString();
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Deleting, inputRef: uploadId);
        var artifactId = Guid.NewGuid();
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _artifactRepository.Setup(r => r.GetByIngestionJobIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new IndexedArtifactDto { IndexedArtifactId = artifactId, UploadId = uploadId }]);
        _chunkRepository.Setup(r => r.DeleteByArtifactIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("chunk rows unavailable"));
        var sut = CreateSut();

        await sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        _artifactRepository.Verify(r => r.DeleteByIdsAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == artifactId),
            It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteJobCleanupAsync_WhenArtifactLookupFails_RollsBackToFailedAndPersistsState() {
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Deleting, inputRef: Guid.NewGuid().ToString());
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _artifactRepository.Setup(r => r.GetByIngestionJobIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("artifact lookup failed"));
        var sut = CreateSut();

        var act = () => sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("artifact lookup failed");
        job.Status.Should().Be(IngestionJobStatus.Failed);
        job.FailureReason.Should().Contain("Background cleanup failed: artifact lookup failed");
        _repository.Verify(r => r.UpdateAsync(job, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ExecuteJobCleanupAsync_WhenFinalJobDeletionFails_RollsBackToFailedAndPersistsState() {
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Deleting, inputRef: Guid.NewGuid().ToString());
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _repository.Setup(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("job row delete failed"));
        var sut = CreateSut();

        var act = () => sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("job row delete failed");
        job.Status.Should().Be(IngestionJobStatus.Failed);
        _repository.Verify(r => r.UpdateAsync(job, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task DeleteAssociatedAssetsAsync_WithFallbackChunkIds_DeletesSearchDocuments() {
        var uploadId = Guid.NewGuid().ToString();
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Deleting, inputRef: uploadId, expectedChunkCount: 2);
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        await sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        _searchDocumentService.Verify(s => s.DeleteDocumentsAsync(It.Is<IEnumerable<string>>(ids => ids.Contains($"{uploadId}-pdf-0") && ids.Contains($"{uploadId}-pdf-1"))), Times.Once);
    }

    [Fact]
    public async Task DeleteAssociatedAssetsAsync_WithCsvFallbackChunkIds_DeletesSearchDocuments() {
        var uploadId = Guid.NewGuid().ToString();
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Deleting, inputType: IngestionJobType.StructuredSpecification, inputRef: uploadId, expectedChunkCount: 1);
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        await sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        _searchDocumentService.Verify(s => s.DeleteDocumentsAsync(It.Is<IEnumerable<string>>(ids => ids.Contains($"{uploadId}-csv-0"))), Times.Once);
    }

    [Fact]
    public async Task DeleteAssociatedAssetsAsync_BikeGraphWithExpectedChunks_DoesNotBuildDocumentFallbackIds() {
        var uploadId = Guid.NewGuid().ToString();
        var job = MakeJob(
            Guid.NewGuid(),
            IngestionJobStatus.Deleting,
            inputType: IngestionJobType.BikeGraph,
            inputRef: uploadId,
            expectedChunkCount: 2);
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        await sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        _searchDocumentService.Verify(
            s => s.DeleteDocumentsAsync(It.IsAny<IEnumerable<string>>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteAssociatedAssetsAsync_WithNonGuidUploadRef_SkipsGraphCleanup() {
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Deleting, inputRef: "no-upload-id");
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        await sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        // Blob cleanup is always attempted when InputRef is non-empty (enforced by entity factory).
        _blobStorage.Verify(b => b.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        // Graph cleanup is skipped when InputRef does not parse as a GUID.
        _graphRepository.Verify(g => g.DeleteByDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteJobAsync_WhenJobNotFound_ThrowsNotFoundError() {
        var jobId = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync((IngestionJob?)null);
        var sut = CreateSut();

        var act = () => sut.DeleteJobAsync(jobId, TestUserId, CancellationToken.None);

        await act.Should().ThrowAsync<DeleteJobException>()
            .Where(ex => ex.Error == DeleteJobError.NotFound)
            .WithMessage($"*'{jobId}' not found*");
    }

    [Fact]
    public async Task DeleteJobAsync_CompletedJob_AtomicallyQueuesCleanup() {
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Completed);
        _repository.Setup(r => r.GetByIdAsync(job.IngestionJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        await sut.DeleteJobAsync(job.IngestionJobId, TestUserId, CancellationToken.None);

        _repository.Verify(r => r.TrySetDeletingAsync(job.IngestionJobId, CancellationToken.None), Times.Once);
        _repository.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteJobAsync_WhenAtomicDeletionTransitionFails_ThrowsConcurrentModificationError() {
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Completed);
        _repository.Setup(r => r.GetByIdAsync(job.IngestionJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _repository.Setup(r => r.TrySetDeletingAsync(job.IngestionJobId, CancellationToken.None)).ReturnsAsync(false);
        var sut = CreateSut();

        var act = () => sut.DeleteJobAsync(job.IngestionJobId, TestUserId, CancellationToken.None);

        await act.Should().ThrowAsync<DeleteJobException>()
            .Where(ex => ex.Error == DeleteJobError.ConcurrentModification);
    }

    [Fact]
    public async Task TransitionStageAsync_CancelledStage_CancelsJobAndSetsCounts() {
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Processing, currentStage: "chunking");
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        var result = await sut.TransitionStageAsync(jobId, new IngestionJobStageRequest { Stage = "cancelled", ChunksProcessed = 5, TotalChunks = 10, FailureReason = "user stopped" }, CancellationToken.None);

        result.Status.Should().Be(IngestionJobStatus.Cancelled.ToString());
        job.Status.Should().Be(IngestionJobStatus.Cancelled);
        job.ExpectedChunkCount.Should().Be(10);
        job.IndexedChunkCount.Should().Be(5);
    }

    [Fact]
    public async Task TransitionStageAsync_RegularStage_UpdatesStageWithoutChangingStatus() {
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Processing, currentStage: "chunking");
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        var result = await sut.TransitionStageAsync(jobId, new IngestionJobStageRequest { Stage = "indexing", ChunksProcessed = 5, TotalChunks = 10 }, CancellationToken.None);

        result.Status.Should().Be(IngestionJobStatus.Processing.ToString());
        result.CurrentStage.Should().Be("indexing");
        job.CurrentStage.Should().Be("indexing");
    }

    [Fact]
    public async Task TransitionStageAsync_ControlCharactersInStageAndFailureReason_LogsSanitizedStructuredFields() {
        // The service now sanitizes Stage/FailureReason inline via LogSanitizer.Sanitize before
        // logging (CWE-117 log-forging defense-in-depth alongside the runtime
        // SanitizingLoggerProvider), so the spy CapturingLogger observes the escaped values, not
        // the raw ones.
        const string attackerStage = "chunk\\name\r\nforged\tentry";
        const string attackerFailureReason = "embedding\\failure\r\nforged\tentry";
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Processing, currentStage: "queued");
        var logger = new CapturingLogger<IngestionJobService>();
        _repository.Setup(r => r.GetByIdAsync(job.IngestionJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut(logger);

        await sut.TransitionStageAsync(
            job.IngestionJobId,
            new IngestionJobStageRequest { Stage = attackerStage, FailureReason = attackerFailureReason },
            CancellationToken.None);

        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.LogLevel.Should().Be(LogLevel.Information);
        entry.Properties.Should().ContainKeys("JobId", "Stage", "ChunksProcessed", "TotalChunks", "FailureReason", "{OriginalFormat}");
        entry.Properties["{OriginalFormat}"].Should().Be(
            "Ingestion job {JobId} stage transitioned to {Stage} (chunks={ChunksProcessed}/{TotalChunks}, failureReason={FailureReason}).");
        entry.Properties["Stage"].Should().Be(LogSanitizer.Sanitize(attackerStage));
        entry.Properties["FailureReason"].Should().Be(LogSanitizer.Sanitize(attackerFailureReason));
    }

    [Fact]
    public async Task TransitionStageAsync_NonTerminalStageWithFailureReasonAndLocalProvider_FailsJob() {
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Processing, currentStage: "chunking", computeProvider: "LocalProcessor");
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        var result = await sut.TransitionStageAsync(jobId, new IngestionJobStageRequest { Stage = "chunking", FailureReason = "bad dims" }, CancellationToken.None);

        result.Status.Should().Be(IngestionJobStatus.Failed.ToString());
        result.CurrentStage.Should().Be("failed");
    }

    [Fact]
    public async Task GetPendingStorageFilesAsync_403WithoutStatus_StillReturnsEmpty() {
        _blobStorage.Setup(b => b.ListAsync("raw-uploads", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException("forbidden"));
        var sut = CreateSut();

        var act = () => sut.GetPendingStorageFilesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<RequestFailedException>();
    }

    [Fact]
    public async Task EnsureGraphArtifactsExistAsync_WhenBlobExists_DoesNotThrow() {
        const string uploadId = "upload-exists";
        _blobStorage.Setup(b => b.ExistsAsync("raw-uploads", $"graph-entities/{uploadId}/entities.json", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var sut = CreateSut();

        var result = await sut.ImportGraphArtifactsAsync(new GraphImportStartRequest { UploadId = uploadId }, TestUserId, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData("spec-dataset", IngestionJobType.StructuredSpecification)]
    [InlineData("bike-graph", IngestionJobType.BikeGraph)]
    public async Task StartJobAsync_MapsDocumentTypeToInputType(string documentType, IngestionJobType expectedType) {
        var request = new IngestionJobStartRequest { DocumentType = documentType, UploadId = "u-1", ProcessorRunId = "run-1", SourceFileName = "file.csv" };
        var sut = CreateSut();

        var result = await sut.StartJobAsync(request, TestUserId, CancellationToken.None);

        result.InputType.Should().Be(expectedType.ToString());
    }

    [Fact]
    public async Task StartJobAsync_UnsupportedDocumentType_ThrowsArgumentException() {
        var request = new IngestionJobStartRequest { DocumentType = "unsupported", UploadId = "u-1", ProcessorRunId = "run-1" };
        var sut = CreateSut();

        var act = () => sut.StartJobAsync(request, TestUserId, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("documentType");
    }

    [Fact]
    public async Task GetPendingStorageFilesAsync_SkipsBlobsWithInvalidNames() {
        var blobs = new List<BlobObjectDescriptor> {
            new() { Name = null!, SizeBytes = 1, LastModifiedUtc = DateTime.UtcNow },
            new() { Name = "   ", SizeBytes = 1, LastModifiedUtc = DateTime.UtcNow },
            new() { Name = "valid.pdf", SizeBytes = 1, LastModifiedUtc = DateTime.UtcNow },
            new() { Name = "upload/source.pdf", SizeBytes = 1, LastModifiedUtc = DateTime.UtcNow }
        };
        _blobStorage.Setup(b => b.ListAsync("raw-uploads", It.IsAny<CancellationToken>())).ReturnsAsync(blobs);
        _repository.Setup(r => r.GetLatestByInputRefsAsync(It.IsAny<IReadOnlyList<(string, IngestionJobType)>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IngestionJob>());
        var sut = CreateSut();

        var result = await sut.GetPendingStorageFilesAsync(CancellationToken.None);

        result.Should().ContainSingle().Which.UploadId.Should().Be("upload");
    }

    [Fact]
    public async Task GetPendingStorageFilesAsync_CsvBlobWithoutUploadId_SkipsEntry() {
        var blobs = new List<BlobObjectDescriptor> {
            new() { Name = ".csv", SizeBytes = 1, LastModifiedUtc = DateTime.UtcNow }
        };
        _blobStorage.Setup(b => b.ListAsync("raw-uploads", It.IsAny<CancellationToken>())).ReturnsAsync(blobs);
        _repository.Setup(r => r.GetLatestByInputRefsAsync(It.IsAny<IReadOnlyList<(string, IngestionJobType)>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IngestionJob>());
        var sut = CreateSut();

        var result = await sut.GetPendingStorageFilesAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPendingStorageFilesAsync_BlobNameNormalizesToEmpty_SkipsEntry() {
        var blobs = new List<BlobObjectDescriptor> {
            new() { Name = "//", SizeBytes = 1, LastModifiedUtc = DateTime.UtcNow },
            new() { Name = @"\\", SizeBytes = 1, LastModifiedUtc = DateTime.UtcNow }
        };
        _blobStorage.Setup(b => b.ListAsync("raw-uploads", It.IsAny<CancellationToken>())).ReturnsAsync(blobs);
        _repository.Setup(r => r.GetLatestByInputRefsAsync(It.IsAny<IReadOnlyList<(string, IngestionJobType)>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IngestionJob>());
        var sut = CreateSut();

        var result = await sut.GetPendingStorageFilesAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitManualMetadataAsync_InvalidJsonObject_ThrowsArgumentException() {
        var sut = CreateSut();
        var act = () => sut.SubmitManualMetadataAsync(Guid.NewGuid(), "[]", TestUserId, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*metadataJson must be a JSON object*");
    }

    [Fact]
    public async Task SubmitManualMetadataAsync_MissingRequiredFields_ListsMissingFields() {
        var sut = CreateSut();
        var act = () => sut.SubmitManualMetadataAsync(Guid.NewGuid(), "{\"make\":\"Honda\"}", TestUserId, CancellationToken.None);
        (await act.Should().ThrowAsync<ArgumentException>())
            .WithMessage("*Missing: model, year, category*");
    }

    [Fact]
    public async Task GetJobMetadataAsync_NonObjectMetadata_ReturnsRawJsonWithIncompleteFlag() {
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Queued, metadataJson: "[]");
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        var result = await sut.GetJobMetadataAsync(jobId, TestUserId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.IsComplete.Should().BeFalse();
        result.RawJson.Should().Be("[]");
    }

    [Fact]
    public async Task GetJobMetadataAsync_YearAsString_ParsesNumericYear() {
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Queued, metadataJson: "{\"make\":\"Honda\",\"model\":\"CBR\",\"year\":\"2023\",\"category\":\"sport\"}");
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        var result = await sut.GetJobMetadataAsync(jobId, TestUserId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Year.Should().Be(2023);
        result.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task GetJobMetadataAsync_YearAsInvalidString_ParsesAsNull() {
        var job = MakeJob(Guid.NewGuid(), IngestionJobStatus.Queued, metadataJson: "{\"make\":\"Honda\",\"model\":\"CBR\",\"year\":\"n/a\",\"category\":\"sport\"}");
        var jobId = job.IngestionJobId;
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        var result = await sut.GetJobMetadataAsync(jobId, TestUserId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Year.Should().BeNull();
    }

    [Fact]
    public void PrivateHelpers_DeadCodePaths_ExecuteWithoutError() {
        var sutType = typeof(IngestionJobService);
        var instance = CreateSut();

        var isFailedStatus = sutType.GetMethod("IsFailedStatus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        isFailedStatus.Invoke(null, [IngestionJobStatus.Failed]).Should().Be(true);
        isFailedStatus.Invoke(null, [IngestionJobStatus.Queued]).Should().Be(false);

        var getPrimaryInputType = sutType.GetMethod("GetPrimaryInputType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        getPrimaryInputType.Invoke(null, ["manual-pdf"]).Should().Be(IngestionJobType.PDFManual);
        var unsupportedAct = () => getPrimaryInputType.Invoke(null, ["unknown"]);
        unsupportedAct.Should().Throw<TargetInvocationException>().WithInnerException<ArgumentException>();

        var applyJobFailure = sutType.GetMethod("ApplyJobFailure", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var job = IngestionJob.Create(IngestionJobType.PDFManual, "dead-code", createdBySubject: null);
        applyJobFailure.Invoke(null, [job, "  "]);
        job.FailureReason.Should().BeNullOrEmpty();

        var firstFailureLine = sutType.GetMethod("FirstFailureLine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        firstFailureLine.Invoke(null, ["line1\nline2"])!.Should().Be("line1");
        firstFailureLine.Invoke(null, ["  \n  "])!.Should().Be("");
        firstFailureLine.Invoke(null, ["single"])!.Should().Be("single");

        var getMissing = sutType.GetMethod("GetMissingMetadataFields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var parsedMetadataType = sutType.GetNestedType("ParsedMetadata", System.Reflection.BindingFlags.NonPublic)!;
        var parsed = System.Activator.CreateInstance(parsedMetadataType, [null, "CBR", 2023, "sport", Array.Empty<string>(), 0.75, false])!;
        var missing = (List<string>)getMissing.Invoke(null, [parsed])!;
        missing.Should().Contain("make");
    }
}
