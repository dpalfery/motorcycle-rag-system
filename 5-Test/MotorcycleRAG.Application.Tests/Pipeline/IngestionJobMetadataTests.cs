using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.UnitTests.Pipeline;

/// <summary>
/// Unit tests for the manual metadata submission and retrieval logic in
/// <see cref="IngestionJobService"/>. Covers happy path, not-found, invalid/incomplete
/// metadata, idempotent duplicates, corrupt stored JSON, and the needs-manual-metadata
/// stage transition.
/// </summary>
public sealed class IngestionJobMetadataTests
{
    private const string TestUserId = "admin-oid-123";
    private const string CompleteMetadataJson =
        """{"make":"Honda","model":"CBR600RR","year":2023,"category":"sport","tags":["abs","fi"]}""";

    private readonly Mock<IIngestionJobRepository> _repository;
    private readonly Mock<IBlobStorageService> _blobStorage;
    private readonly Mock<IIndexedArtifactRepository> _artifactRepository;
    private readonly Mock<IIndexedChunkRepository> _chunkRepository;
    private readonly Mock<IAzureSearchDocumentService> _searchDocumentService;
    private readonly Mock<IGraphRepository> _graphRepository;
    private readonly Mock<IGraphEntityIngestionService> _graphEntityIngestionService;
    private readonly GraphIngestionChannel _graphIngestionChannel;
    private readonly Mock<ILogger<IngestionJobService>> _logger;

    public IngestionJobMetadataTests()
    {
        _repository = new Mock<IIngestionJobRepository>();
        _blobStorage = new Mock<IBlobStorageService>();
        _artifactRepository = new Mock<IIndexedArtifactRepository>();
        _chunkRepository = new Mock<IIndexedChunkRepository>();
        _searchDocumentService = new Mock<IAzureSearchDocumentService>();
        _graphRepository = new Mock<IGraphRepository>();
        _graphEntityIngestionService = new Mock<IGraphEntityIngestionService>();
        _graphIngestionChannel = new GraphIngestionChannel();
        _logger = new Mock<ILogger<IngestionJobService>>();

        // Default: UpdateMetadataAsync and UpdateAsync succeed.
        _repository
            .Setup(r => r.UpdateMetadataAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository
            .Setup(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository
            .Setup(r => r.UpdateStageAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // === SubmitManualMetadataAsync ===

    [Fact]
    public async Task SubmitManualMetadataAsync_HappyPath_PersistsMetadataAndTransitionsToProcessing()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var job = CreateJob(jobId, IngestionJobStatus.AwaitingMetadata);

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _repository
            .Setup(r => r.TryTransitionFromAwaitingMetadataAsync(jobId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut();

        // Act
        var result = await sut.SubmitManualMetadataAsync(jobId, CompleteMetadataJson, TestUserId);

        // Assert
        result.Status.Should().Be("Processing");
        result.CurrentStage.Should().Be("resuming");

        _repository.Verify(
            r => r.UpdateMetadataAsync(jobId, CompleteMetadataJson, It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(
            r => r.TryTransitionFromAwaitingMetadataAsync(jobId, "resuming", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SubmitManualMetadataAsync_JobNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange — H1: KeyNotFoundException (not InvalidOperationException) so the controller
        // can distinguish "not found" (404) from DB failures (500).
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);

        var sut = CreateSut();

        // Act
        var act = () => sut.SubmitManualMetadataAsync(jobId, CompleteMetadataJson, TestUserId);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{jobId}*");

        // Metadata must NOT be persisted when the job doesn't exist.
        _repository.Verify(
            r => r.UpdateMetadataAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitManualMetadataAsync_InvalidJson_ThrowsArgumentException()
    {
        var sut = CreateSut();

        var act = () => sut.SubmitManualMetadataAsync(Guid.NewGuid(), "{not valid json", TestUserId);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not valid JSON*");
    }

    [Fact]
    public async Task SubmitManualMetadataAsync_IncompleteMetadata_ThrowsArgumentExceptionListingMissingFields()
    {
        // Arrange — M3: only make is provided; model, year, category are missing.
        var incompleteJson = """{"make":"Honda"}""";

        var sut = CreateSut();

        // Act
        var act = () => sut.SubmitManualMetadataAsync(Guid.NewGuid(), incompleteJson, TestUserId);

        // Assert
        (await act.Should().ThrowAsync<ArgumentException>())
            .WithMessage("*incomplete*model*year*category*");
    }

    [Fact]
    public async Task SubmitManualMetadataAsync_IdempotentDuplicate_PersistsMetadataWithoutResume()
    {
        // Arrange — job is already past AwaitingMetadata (e.g. Processing from a prior resume).
        var jobId = Guid.NewGuid();
        var job = CreateJob(jobId, IngestionJobStatus.Processing);

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        // CAS returns false: the row was not in AwaitingMetadata.
        _repository
            .Setup(r => r.TryTransitionFromAwaitingMetadataAsync(jobId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();

        // Act
        var result = await sut.SubmitManualMetadataAsync(jobId, CompleteMetadataJson, TestUserId);

        // Assert: metadata was persisted but the resume was not re-triggered.
        result.Status.Should().Be("Processing");

        _repository.Verify(
            r => r.UpdateMetadataAsync(jobId, CompleteMetadataJson, It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(
            r => r.TryTransitionFromAwaitingMetadataAsync(jobId, "resuming", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SubmitManualMetadataAsync_EmptyUserId_ThrowsArgumentException()
    {
        // Arrange — M2: userId is required.
        var sut = CreateSut();

        // Act
        var act = () => sut.SubmitManualMetadataAsync(Guid.NewGuid(), CompleteMetadataJson, "");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubmitManualMetadataAsync_TooLongJson_ThrowsArgumentException()
    {
        var sut = CreateSut();
        var tooLong = "{\"make\":\"a\",\"model\":\"b\",\"year\":1,\"category\":\"c\",\"padding\":\""
                      + new string('x', 10_100) + "\"}";

        var act = () => sut.SubmitManualMetadataAsync(Guid.NewGuid(), tooLong, TestUserId);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*10000*");
    }

    // === GetJobMetadataAsync ===

    [Fact]
    public async Task GetJobMetadataAsync_NoMetadata_ReturnsEmptyResponseWithIsCompleteFalse()
    {
        var jobId = Guid.NewGuid();
        var job = CreateJob(jobId, IngestionJobStatus.AwaitingMetadata);

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var sut = CreateSut();

        var result = await sut.GetJobMetadataAsync(jobId, TestUserId);

        result.Should().NotBeNull();
        result!.IsComplete.Should().BeFalse();
        result.FillRate.Should().Be(0.0);
        result.Make.Should().BeNull();
        result.RawJson.Should().BeNull();
    }

    [Fact]
    public async Task GetJobMetadataAsync_CorruptStoredJson_ReturnsRawBlobWithIsCompleteFalse()
    {
        var jobId = Guid.NewGuid();
        var corruptJson = "{ this is not valid";
        var job = CreateJob(jobId, IngestionJobStatus.AwaitingMetadata, metadataJson: corruptJson);

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var sut = CreateSut();

        var result = await sut.GetJobMetadataAsync(jobId, TestUserId);

        result.Should().NotBeNull();
        result!.IsComplete.Should().BeFalse();
        result.RawJson.Should().Be(corruptJson);
    }

    [Fact]
    public async Task GetJobMetadataAsync_CompleteMetadata_ReturnsAllFieldsWithIsCompleteTrue()
    {
        var jobId = Guid.NewGuid();
        var job = CreateJob(jobId, IngestionJobStatus.Processing, metadataJson: CompleteMetadataJson);

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var sut = CreateSut();

        var result = await sut.GetJobMetadataAsync(jobId, TestUserId);

        result.Should().NotBeNull();
        result!.Make.Should().Be("Honda");
        result.Model.Should().Be("CBR600RR");
        result.Year.Should().Be(2023);
        result.Category.Should().Be("sport");
        result.Tags.Should().Equal("abs", "fi");
        result.FillRate.Should().Be(1.0);
        result.IsComplete.Should().BeTrue();
        result.RawJson.Should().Be(CompleteMetadataJson);
    }

    [Fact]
    public async Task GetJobMetadataAsync_PartialMetadata_ReturnsPopulatedFieldsWithIsCompleteFalse()
    {
        var jobId = Guid.NewGuid();
        var partialJson = """{"make":"Yamaha","model":"MT-07"}""";
        var job = CreateJob(jobId, IngestionJobStatus.AwaitingMetadata, metadataJson: partialJson);

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var sut = CreateSut();

        var result = await sut.GetJobMetadataAsync(jobId, TestUserId);

        result.Should().NotBeNull();
        result!.Make.Should().Be("Yamaha");
        result.Model.Should().Be("MT-07");
        result.Year.Should().BeNull();
        result.Category.Should().BeNull();
        result.FillRate.Should().Be(0.5);
        result.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetJobMetadataAsync_JobNotFound_ReturnsNull()
    {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);

        var sut = CreateSut();

        var result = await sut.GetJobMetadataAsync(jobId, TestUserId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetJobMetadataAsync_EmptyUserId_ThrowsArgumentException()
    {
        var sut = CreateSut();

        var act = () => sut.GetJobMetadataAsync(Guid.NewGuid(), "  ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // === TransitionStageAsync with needs-manual-metadata ===

    [Fact]
    public async Task TransitionStageAsync_NeedsManualMetadata_TransitionsToAwaitingMetadata()
    {
        var jobId = Guid.NewGuid();
        var job = CreateJob(jobId, IngestionJobStatus.Processing, currentStage: "extracting-metadata");

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var sut = CreateSut();

        // NOTE: FailureReason is intentionally omitted — a non-empty failure reason on a
        // local-processor job would trigger ShouldTreatLocalProcessorFailureAsTerminal,
        // routing to the Failed branch instead of AwaitingMetadata. The needs-manual-metadata
        // stage is informational, not a failure.
        var request = new IngestionJobStageRequest
        {
            Stage = "needs-manual-metadata"
        };

        var result = await sut.TransitionStageAsync(jobId, request);

        result.Status.Should().Be("AwaitingMetadata");
        result.CurrentStage.Should().Be("needs-manual-metadata");
        result.FailureReason.Should().Contain("Manual entry required");

        _repository.Verify(
            r => r.UpdateAsync(It.Is<IngestionJob>(j => j.Status == IngestionJobStatus.AwaitingMetadata), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // === Helpers ===

    private IngestionJobService CreateSut()
    {
        return new IngestionJobService(
            _repository.Object,
            _blobStorage.Object,
            _artifactRepository.Object,
            _chunkRepository.Object,
            _searchDocumentService.Object,
            _graphRepository.Object,
            _graphEntityIngestionService.Object,
            _graphIngestionChannel,
            Options.Create(new BlobStorageOptions { RawUploadsContainer = "raw-uploads" }),
            Options.Create(new IngestionOptions { MaxInputBytes = 2_000_000_000L }),
            _logger.Object);
    }

    private static IngestionJob CreateJob(
        Guid jobId,
        IngestionJobStatus status,
        string? metadataJson = null,
        string? currentStage = null)
    {
        return IngestionJob.Rehydrate(
            id: 1,
            ingestionJobId: jobId,
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: null,
            completedAtUtc: null,
            createdBySubject: null,
            status: status,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: IngestionJobType.PDFManual,
            inputRef: "upload-test-123",
            sourceFileName: null,
            computeProvider: "AdminLocalProcessor",
            docIngestionRunId: null,
            manualDocumentId: null,
            totalPages: null,
            pagesCapturedViewableCount: null,
            pagesWithSearchableTextCount: null,
            pagesWithOcrTextCount: null,
            pagesWithNativeTextCount: null,
            missingPagesJson: null,
            metricsJson: null,
            expectedChunkCount: null,
            indexedChunkCount: null,
            currentStage: currentStage,
            stageSetAtUtc: null,
            metadataJson: metadataJson);
    }
}
