using Azure;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
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
        _graphIngestionChannel = new GraphIngestionChannel();
        _logger = new Mock<ILogger<IngestionJobService>>();

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

    private IngestionJobService CreateSut() => new(
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

    [Fact]
    public async Task GetRecentIngestionJobsAsync_MaxCountLessThanOrEqualZero_ThrowsArgumentOutOfRangeException() {
        var sut = CreateSut();
        var act = () => sut.GetRecentIngestionJobsAsync(0, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("maxCount");
    }

    [Fact]
    public async Task GetRecentIngestionJobsAsync_MapsMissingPagesJson() {
        var job = new IngestionJob {
            IngestionJobId = Guid.NewGuid(),
            Status = IngestionJobStatus.Completed,
            MissingPagesJson = "[1,2,3]"
        };
        _repository.Setup(r => r.GetRecentAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync([job]);
        var sut = CreateSut();

        var result = await sut.GetRecentIngestionJobsAsync(50, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].MissingPages.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task GetRecentIngestionJobsAsync_MalformedMissingPagesJson_ReturnsEmpty() {
        var job = new IngestionJob {
            IngestionJobId = Guid.NewGuid(),
            Status = IngestionJobStatus.Completed,
            MissingPagesJson = "not valid json"
        };
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
        var jobId = Guid.NewGuid();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Processing,
            InputType = IngestionJobType.PDFManual
        };
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
    public async Task FailJobAsync_NonTerminalJob_FailsAndThrowsBecauseStageCannotBeUpdatedAfterTerminalTransition() {
        var jobId = Guid.NewGuid();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Processing,
            InputType = IngestionJobType.PDFManual
        };
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        var act = () => sut.FailJobAsync(jobId, "processor error", TestUserId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot update its stage*");
        job.Status.Should().Be(IngestionJobStatus.Failed);
    }

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
        var job = new IngestionJob {
            IngestionJobId = Guid.NewGuid(),
            InputRef = "upload-graph-002",
            Status = IngestionJobStatus.Processing
        };
        var sut = CreateSut();

        await sut.ProcessGraphIngestionJobAsync(job);

        job.Status.Should().Be(IngestionJobStatus.Completed);
        _graphEntityIngestionService.Verify(g => g.IngestAsync("upload-graph-002", CancellationToken.None), Times.Once);
        _repository.Verify(r => r.UpdateAsync(job, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ProcessGraphIngestionJobAsync_WhenIngestionFails_FailsJobAndRecordsFailure() {
        var job = new IngestionJob {
            IngestionJobId = Guid.NewGuid(),
            InputRef = "upload-graph-003",
            Status = IngestionJobStatus.Processing
        };
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

    [Fact]
    public async Task ExecuteJobCleanupAsync_RunBestEffortFailure_LogsWarningAndContinues() {
        var jobId = Guid.NewGuid();
        var uploadId = Guid.NewGuid().ToString();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Deleting,
            InputType = IngestionJobType.PDFManual,
            InputRef = uploadId,
            ExpectedChunkCount = 1
        };
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _searchDocumentService.Setup(s => s.DeleteDocumentsAsync(It.IsAny<IEnumerable<string>>())).ThrowsAsync(new InvalidOperationException("search down"));
        var sut = CreateSut();

        await sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        _repository.Verify(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAssociatedAssetsAsync_WithFallbackChunkIds_DeletesSearchDocuments() {
        var jobId = Guid.NewGuid();
        var uploadId = Guid.NewGuid().ToString();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Deleting,
            InputType = IngestionJobType.PDFManual,
            InputRef = uploadId,
            ExpectedChunkCount = 2
        };
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        await sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        _searchDocumentService.Verify(s => s.DeleteDocumentsAsync(It.Is<IEnumerable<string>>(ids => ids.Contains($"{uploadId}-pdf-0") && ids.Contains($"{uploadId}-pdf-1"))), Times.Once);
    }

    [Fact]
    public async Task DeleteAssociatedAssetsAsync_WithCsvFallbackChunkIds_DeletesSearchDocuments() {
        var jobId = Guid.NewGuid();
        var uploadId = Guid.NewGuid().ToString();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Deleting,
            InputType = IngestionJobType.StructuredSpecification,
            InputRef = uploadId,
            ExpectedChunkCount = 1
        };
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        await sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        _searchDocumentService.Verify(s => s.DeleteDocumentsAsync(It.Is<IEnumerable<string>>(ids => ids.Contains($"{uploadId}-csv-0"))), Times.Once);
    }

    [Fact]
    public async Task DeleteAssociatedAssetsAsync_WithoutUploadId_SkipsBlobAndGraphCleanup() {
        var jobId = Guid.NewGuid();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Deleting,
            InputType = IngestionJobType.PDFManual,
            InputRef = null!
        };
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        await sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        _blobStorage.Verify(b => b.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task TransitionStageAsync_CancelledStage_CancelsJobAndSetsCounts() {
        var jobId = Guid.NewGuid();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Processing,
            InputType = IngestionJobType.PDFManual,
            CurrentStage = "chunking"
        };
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
        var jobId = Guid.NewGuid();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Processing,
            InputType = IngestionJobType.PDFManual,
            CurrentStage = "chunking"
        };
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        var result = await sut.TransitionStageAsync(jobId, new IngestionJobStageRequest { Stage = "indexing", ChunksProcessed = 5, TotalChunks = 10 }, CancellationToken.None);

        result.Status.Should().Be(IngestionJobStatus.Processing.ToString());
        result.CurrentStage.Should().Be("indexing");
        job.CurrentStage.Should().Be("indexing");
    }

    [Fact]
    public async Task TransitionStageAsync_NonTerminalStageWithFailureReasonAndLocalProvider_FailsJob() {
        var jobId = Guid.NewGuid();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Processing,
            InputType = IngestionJobType.PDFManual,
            CurrentStage = "chunking",
            ComputeProvider = "LocalProcessor"
        };
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
        var jobId = Guid.NewGuid();
        var job = new IngestionJob { IngestionJobId = jobId, MetadataJson = "[]" };
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        var result = await sut.GetJobMetadataAsync(jobId, TestUserId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.IsComplete.Should().BeFalse();
        result.RawJson.Should().Be("[]");
    }

    [Fact]
    public async Task GetJobMetadataAsync_YearAsString_ParsesNumericYear() {
        var jobId = Guid.NewGuid();
        var job = new IngestionJob { IngestionJobId = jobId, MetadataJson = "{\"make\":\"Honda\",\"model\":\"CBR\",\"year\":\"2023\",\"category\":\"sport\"}" };
        _repository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var sut = CreateSut();

        var result = await sut.GetJobMetadataAsync(jobId, TestUserId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Year.Should().Be(2023);
        result.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task GetJobMetadataAsync_YearAsInvalidString_ParsesAsNull() {
        var jobId = Guid.NewGuid();
        var job = new IngestionJob { IngestionJobId = jobId, MetadataJson = "{\"make\":\"Honda\",\"model\":\"CBR\",\"year\":\"n/a\",\"category\":\"sport\"}" };
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
        var job = new IngestionJob { IngestionJobId = Guid.NewGuid() };
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
