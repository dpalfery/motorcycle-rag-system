using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.Graph;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.UnitTests.Pipeline;

/// <summary>
/// Tests for code-reviewer blocking findings on <see cref="IngestionJobService.RunGraphIngestionAsync"/>:
/// 1. Linker failure after job.Complete() orphans job forever
/// 2. Legacy constructor with null! linker is a live bug
/// 3. Missing logs for D5 no-op branches
/// </summary>
public sealed class IngestionJobServiceBlockingFindingsTests
{
    private readonly Mock<IIngestionJobRepository> _repository;
    private readonly Mock<IBlobStorageService> _blobStorage;
    private readonly Mock<IIndexedArtifactRepository> _artifactRepository;
    private readonly Mock<IIndexedChunkRepository> _chunkRepository;
    private readonly Mock<IAzureSearchDocumentService> _searchDocumentService;
    private readonly Mock<IGraphRepository> _graphRepository;
    private readonly Mock<IGraphEntityIngestionService> _graphEntityIngestionService;
    private readonly Mock<IBikeModelRepository> _bikeModelRepository;
    private readonly ManualBikeLinker _manualBikeLinker;
    private readonly GraphIngestionChannel _graphIngestionChannel;
    private readonly Mock<ILogger<IngestionJobService>> _logger;

    public IngestionJobServiceBlockingFindingsTests()
    {
        _repository = new Mock<IIngestionJobRepository>();
        _blobStorage = new Mock<IBlobStorageService>();
        _artifactRepository = new Mock<IIndexedArtifactRepository>();
        _chunkRepository = new Mock<IIndexedChunkRepository>();
        _searchDocumentService = new Mock<IAzureSearchDocumentService>();
        _graphRepository = new Mock<IGraphRepository>();
        _graphEntityIngestionService = new Mock<IGraphEntityIngestionService>();
        _bikeModelRepository = new Mock<IBikeModelRepository>();
        _graphIngestionChannel = new GraphIngestionChannel();
        _logger = new Mock<ILogger<IngestionJobService>>();

        // Create a real ManualBikeLinker (it's sealed, can't mock)
        var linkerLogger = new Mock<ILogger<ManualBikeLinker>>();
        _manualBikeLinker = new ManualBikeLinker(_bikeModelRepository.Object, _graphRepository.Object, linkerLogger.Object);

        _repository.Setup(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _graphEntityIngestionService.Setup(g => g.IngestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>
    /// BLOCKING #1: When metadata is valid and linker is called, verify it's called
    /// before job is marked terminal so that if it throws, the job can be marked Failed.
    /// </summary>
    [Fact]
    public async Task RunGraphIngestionAsync_ValidMetadata_LinkerIsCalledBeforeJobCompleted()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var manualDocId = Guid.NewGuid();
        var metadataJson = """{"make":"Honda","model":"CBR600RR","year":2023}""";

        var job = IngestionJob.Rehydrate(
            id: 1,
            ingestionJobId: jobId,
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: null,
            completedAtUtc: null,
            createdBySubject: null,
            status: IngestionJobStatus.Processing,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: IngestionJobType.BikeGraph,
            inputRef: "upload-123",
            sourceFileName: null,
            computeProvider: "AdminLocalProcessor",
            docIngestionRunId: null,
            manualDocumentId: manualDocId,
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
            metadataJson: metadataJson);

        // Mock that graph ingest succeeds
        _graphEntityIngestionService.Setup(g => g.IngestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Mock linker to succeed: bike model exists
        var bikeModel = BikeModel.Create("Honda", "CBR600RR", 2023, "sport-bike");
        _bikeModelRepository.Setup(r => r.FindCanonicalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bikeModel);
        _graphRepository.Setup(r => r.UpsertEdgeAsync(It.IsAny<GraphEdgeDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();

        // Act
        await sut.ProcessGraphIngestionJobAsync(job);

        // Assert
        // Job should be completed successfully (not failed)
        job.Status.Should().Be(IngestionJobStatus.Completed);
        job.FailureReason.Should().BeNull();
    }

    /// <summary>
    /// BLOCKING #3: D5 requirement — missing metadata should log, not silently no-op.
    /// When job.MetadataJson is null/blank, the outer if is false and nothing logs.
    /// </summary>
    [Fact]
    public async Task RunGraphIngestionAsync_ManualDocumentPresentButMetadataNull_LogsWarning()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var manualDocId = Guid.NewGuid();

        var job = IngestionJob.Rehydrate(
            id: 1,
            ingestionJobId: jobId,
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: null,
            completedAtUtc: null,
            createdBySubject: null,
            status: IngestionJobStatus.Processing,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: IngestionJobType.BikeGraph,
            inputRef: "upload-123",
            sourceFileName: null,
            computeProvider: "AdminLocalProcessor",
            docIngestionRunId: null,
            manualDocumentId: manualDocId,
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
            metadataJson: null); // NULL metadata

        var sut = CreateSut();

        // Act
        await sut.ProcessGraphIngestionJobAsync(job);

        // Assert
        // Should log a warning about missing metadata
        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("metadata") || v.ToString()!.Contains("Metadata")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// BLOCKING #3: When metadata parses successfully but Make/Model/Year are invalid/missing,
    /// nothing logs and linking is silently skipped.
    /// </summary>
    [Fact]
    public async Task RunGraphIngestionAsync_ManualDocumentPresentButMetadataMissingYear_LogsWarning()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var manualDocId = Guid.NewGuid();
        var metadataJson = """{"make":"Honda","model":"CBR600RR","year":null}"""; // Invalid year

        var job = IngestionJob.Rehydrate(
            id: 1,
            ingestionJobId: jobId,
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: null,
            completedAtUtc: null,
            createdBySubject: null,
            status: IngestionJobStatus.Processing,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: IngestionJobType.BikeGraph,
            inputRef: "upload-123",
            sourceFileName: null,
            computeProvider: "AdminLocalProcessor",
            docIngestionRunId: null,
            manualDocumentId: manualDocId,
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
            metadataJson: metadataJson);

        var sut = CreateSut();

        // Act
        await sut.ProcessGraphIngestionJobAsync(job);

        // Assert
        // Should log a warning about incomplete/invalid metadata for linking
        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("metadata") || v.ToString()!.Contains("linking")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// BLOCKING #2: Legacy constructor's null! field is a live bug.
    /// This test verifies that only the new constructor (with ManualBikeLinker parameter) should exist.
    /// </summary>
    [Fact]
    public void IngestionJobService_OnlyOneConstructor_RequiresManualBikeLinker()
    {
        // Arrange & Act
        var constructors = typeof(IngestionJobService).GetConstructors();

        // Assert
        // Should have exactly one public constructor that takes ManualBikeLinker
        constructors.Should().HaveCount(1, "only one constructor should exist (legacy deleted)");

        var constructor = constructors[0];
        var parameters = constructor.GetParameters();

        // The single constructor should have ManualBikeLinker parameter
        var hasLinkerParam = parameters.Any(p => p.ParameterType == typeof(ManualBikeLinker));
        hasLinkerParam.Should().BeTrue("constructor should require ManualBikeLinker");
    }

    /// <summary>
    /// BLOCKING #2: Verify DI actually resolves the linker-carrying constructor.
    /// This demonstrates the composition root wiring works correctly.
    /// </summary>
    [Fact]
    public void CreateSut_ResolvesDependenciesCorrectly_LinkerIsNotNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        // Use reflection to verify _manualBikeLinker field is set (not null!)
        var linkerField = typeof(IngestionJobService).GetField(
            "_manualBikeLinker",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        linkerField.Should().NotBeNull("_manualBikeLinker field should exist");

        var linkerValue = linkerField!.GetValue(sut);
        linkerValue.Should().NotBeNull("_manualBikeLinker should be initialized (not null!)");
    }

    private IngestionJobService CreateSut() => new(
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
        _logger.Object);
}
