using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Azure;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.UnitTests.Pipeline;

/// <summary>
/// Unit tests for <see cref="IngestionJobService"/>.
/// Verifies job creation and DB-backed status history for local-first ingestion.
/// </summary>
public sealed class IngestionJobServiceDualModeTests {
    private const string TestUserId = "test-user-oid";
    private const string TestRunId = "run-12345";

    private readonly Mock<IIngestionJobRepository> _repository;
    private readonly Mock<IBlobStorageService> _blobStorage;
    private readonly Mock<IIndexedArtifactRepository> _artifactRepository;
    private readonly Mock<IIndexedChunkRepository> _chunkRepository;
    private readonly Mock<IAzureSearchDocumentService> _searchDocumentService;
    private readonly Mock<IGraphRepository> _graphRepository;
    private readonly Mock<IGraphEntityIngestionService> _graphEntityIngestionService;
    private readonly GraphIngestionChannel _graphIngestionChannel;
    private readonly Mock<ILogger<IngestionJobService>> _logger;

    public IngestionJobServiceDualModeTests() {
        _repository = new Mock<IIngestionJobRepository>();
        _blobStorage = new Mock<IBlobStorageService>();
        _artifactRepository = new Mock<IIndexedArtifactRepository>();
        _chunkRepository = new Mock<IIndexedChunkRepository>();
        _searchDocumentService = new Mock<IAzureSearchDocumentService>();
        _graphRepository = new Mock<IGraphRepository>();
        _graphEntityIngestionService = new Mock<IGraphEntityIngestionService>();
        _graphIngestionChannel = new GraphIngestionChannel();
        _logger = new Mock<ILogger<IngestionJobService>>();

        _repository
            .Setup(r => r.CreateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob job, CancellationToken _) => job);

        _repository
            .Setup(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repository
            .Setup(r => r.TrySetDeletingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _artifactRepository
            .Setup(r => r.GetByIngestionJobIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IndexedArtifact>());
        _artifactRepository
            .Setup(r => r.GetByUploadIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IndexedArtifact>());
        _artifactRepository
            .Setup(r => r.DeleteByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _chunkRepository
            .Setup(r => r.GetByArtifactIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IndexedChunk>());
        _chunkRepository
            .Setup(r => r.GetByIngestionJobIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IndexedChunk>());
        _chunkRepository
            .Setup(r => r.GetByUploadIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IndexedChunk>());
        _chunkRepository
            .Setup(r => r.DeleteByArtifactIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chunkRepository
            .Setup(r => r.DeleteByIngestionJobIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chunkRepository
            .Setup(r => r.DeleteByUploadIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _blobStorage
            .Setup(s => s.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _searchDocumentService
            .Setup(s => s.DeleteDocumentsAsync(It.IsAny<IEnumerable<string>>()))
            .Returns(Task.CompletedTask);
        _graphRepository
            .Setup(r => r.DeleteByDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenRepositoryIsNull() {
        var sut = () => new IngestionJobService(
            null!,
            _blobStorage.Object,
            _artifactRepository.Object,
            _chunkRepository.Object,
            _searchDocumentService.Object,
            _graphRepository.Object,
            _graphEntityIngestionService.Object,
            _graphIngestionChannel,
            CreateBlobOptions(),
            CreateIngestionOptions(),
            _logger.Object);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("repository");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenBlobStorageServiceIsNull() {
        var sut = () => new IngestionJobService(
            _repository.Object,
            null!,
            _artifactRepository.Object,
            _chunkRepository.Object,
            _searchDocumentService.Object,
            _graphRepository.Object,
            _graphEntityIngestionService.Object,
            _graphIngestionChannel,
            CreateBlobOptions(),
            CreateIngestionOptions(),
            _logger.Object);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("blobStorageService");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenGraphEntityIngestionServiceIsNull() {
        var sut = () => new IngestionJobService(
            _repository.Object,
            _blobStorage.Object,
            _artifactRepository.Object,
            _chunkRepository.Object,
            _searchDocumentService.Object,
            _graphRepository.Object,
            null!,
            _graphIngestionChannel,
            CreateBlobOptions(),
            CreateIngestionOptions(),
            _logger.Object);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("graphEntityIngestionService");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenBlobStorageOptionsAreNull() {
        var sut = () => new IngestionJobService(
            _repository.Object,
            _blobStorage.Object,
            _artifactRepository.Object,
            _chunkRepository.Object,
            _searchDocumentService.Object,
            _graphRepository.Object,
            _graphEntityIngestionService.Object,
            _graphIngestionChannel,
            null!,
            CreateIngestionOptions(),
            _logger.Object);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("blobStorageOptions");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenIngestionOptionsAreNull() {
        var sut = () => new IngestionJobService(
            _repository.Object,
            _blobStorage.Object,
            _artifactRepository.Object,
            _chunkRepository.Object,
            _searchDocumentService.Object,
            _graphRepository.Object,
            _graphEntityIngestionService.Object,
            _graphIngestionChannel,
            CreateBlobOptions(),
            null!,
            _logger.Object);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull() {
        var sut = () => new IngestionJobService(
            _repository.Object,
            _blobStorage.Object,
            _artifactRepository.Object,
            _chunkRepository.Object,
            _searchDocumentService.Object,
            _graphRepository.Object,
            _graphEntityIngestionService.Object,
            _graphIngestionChannel,
            CreateBlobOptions(),
            CreateIngestionOptions(),
            null!);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task StartJobAsync_ShouldCreateQueuedJobWithoutTriggeringProcessor() {
        var sut = CreateSut();
        var request = new IngestionJobStartRequest {
            UploadId = "upload-abc",
            DocumentType = "manual-pdf",
            ProcessorRunId = TestRunId
        };

        var response = await sut.StartJobAsync(request, TestUserId);

        response.DocIngestionRunId.Should().Be(TestRunId);
        response.Status.Should().Be(IngestionJobStatus.Queued.ToString());
        response.StartedAtUtc.Should().BeNull();
        response.ComputeProvider.Should().Be("AdminLocalProcessor");
        _repository.Verify(r => r.CreateAsync(It.Is<IngestionJob>(job =>
            job.Status == IngestionJobStatus.Queued
            && job.StartedAtUtc == null
            && job.ComputeProvider == "AdminLocalProcessor"
            && job.DocIngestionRunId == TestRunId), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetJobStatusAsync_ShouldReturnPersistedStatusWithoutPollingProcessor() {
        var jobId = Guid.NewGuid();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Queued,
            InputType = IngestionJobType.StructuredSpecification,
            InputRef = "upload-abc",
            DocIngestionRunId = TestRunId
        };

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var sut = CreateSut();

        var response = await sut.GetJobStatusAsync(jobId, TestUserId);

        response.Should().NotBeNull();
        response!.Status.Should().Be(IngestionJobStatus.Queued.ToString());
        response.DocIngestionRunId.Should().Be(TestRunId);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetJobStatusAsync_BikeGraphJob_ShouldNotImportGraphEntitiesFromStatusRead() {
        var jobId = Guid.NewGuid();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Processing,
            InputType = IngestionJobType.BikeGraph,
            InputRef = "upload-graph-001",
            DocIngestionRunId = TestRunId
        };

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var sut = CreateSut();

        var response = await sut.GetJobStatusAsync(jobId, TestUserId);

        response.Should().NotBeNull();
        response!.Status.Should().Be(IngestionJobStatus.Processing.ToString());
        _graphEntityIngestionService.Verify(g => g.IngestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPendingStorageFilesAsync_WhenBlobListingIsForbidden_ReturnsEmptyList() {
        _blobStorage
            .Setup(b => b.ListAsync("raw-uploads", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "forbidden", "AuthorizationPermissionMismatch", null));

        var sut = CreateSut();

        var result = await sut.GetPendingStorageFilesAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DeletePendingStorageFileAsync_ShouldDeleteHistoryRecords() {
        _repository
            .Setup(r => r.DeleteByInputRefAsync("upload-pending-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var sut = CreateSut();

        await sut.DeletePendingStorageFileAsync("upload-pending-001", "spec-dataset", CancellationToken.None);

        _repository.Verify(
            r => r.DeleteByInputRefAsync("upload-pending-001", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ClearPendingStorageFilesAsync_ShouldDeleteEachPendingUploadOnce() {
        var now = DateTimeOffset.UtcNow;
        _blobStorage
            .Setup(b => b.ListAsync("raw-uploads", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BlobObjectDescriptor {
                    Name = "upload-pending-101.csv",
                    LastModifiedUtc = now
                },
                new BlobObjectDescriptor {
                    Name = "upload-pending-202/source.pdf",
                    LastModifiedUtc = now.AddMinutes(-1)
                }
            ]);

        // GetPendingStorageFilesAsync now batches all (InputRef, InputType) lookups into a
        // single GetLatestByInputRefsAsync call. Returning an empty array means no jobs
        // exist for any pair, so both blobs are treated as pending and eligible for deletion.
        _repository
            .Setup(r => r.GetLatestByInputRefsAsync(
                It.IsAny<IReadOnlyCollection<(string, IngestionJobType)>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IngestionJob>());

        _repository
            .Setup(r => r.DeleteByInputRefAsync("upload-pending-101", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _repository
            .Setup(r => r.DeleteByInputRefAsync("upload-pending-202", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = CreateSut();

        var deletedCount = await sut.ClearPendingStorageFilesAsync(CancellationToken.None);

        deletedCount.Should().Be(2);
        _repository.Verify(r => r.DeleteByInputRefAsync("upload-pending-101", It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.DeleteByInputRefAsync("upload-pending-202", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingStorageFilesAsync_WhenGraphOnlyWorkflowCompleted_ShouldReturnPendingWithGraphStatus() {
        _blobStorage
            .Setup(b => b.ListAsync("raw-uploads", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BlobObjectDescriptor {
                    Name = "upload-graph-complete-001.csv",
                    LastModifiedUtc = DateTimeOffset.UtcNow
                }
            ]);

        var graphJob = new IngestionJob {
            IngestionJobId = Guid.NewGuid(),
            InputRef = "upload-graph-complete-001",
            InputType = IngestionJobType.BikeGraph,
            Status = IngestionJobStatus.Completed
        };

        // GetPendingStorageFilesAsync batches (InputRef, InputType) lookups into a single
        // GetLatestByInputRefsAsync call. Returning only the completed graph job (with no
        // primary StructuredSpecification job) means the primary workflow is pending while
        // the graph import is finished — so the file surfaces with GraphImportStatus set.
        _repository
            .Setup(r => r.GetLatestByInputRefsAsync(
                It.IsAny<IReadOnlyCollection<(string, IngestionJobType)>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { graphJob });

        var sut = CreateSut();

        var result = await sut.GetPendingStorageFilesAsync(CancellationToken.None);

        result.Should().ContainSingle();
        result[0].UploadId.Should().Be("upload-graph-complete-001");
        result[0].GraphImportStatus.Should().Be("Completed");
    }

    [Fact]
    public async Task DeleteJobAsync_WhenJobIsActive_ShouldRejectDelete() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Processing,
                InputType = IngestionJobType.PDFManual,
                InputRef = "upload-active-001"
            });
        var sut = CreateSut();

        var act = async () => await sut.DeleteJobAsync(jobId, TestUserId, CancellationToken.None);

        await act.Should().ThrowAsync<DeleteJobException>()
            .Where(ex => ex.Error == DeleteJobError.Active)
            .WithMessage($"*'{jobId}' is active*");
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.TrySetDeletingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(IngestionJobStatus.Queued)]
    [InlineData(IngestionJobStatus.Completed)]
    [InlineData(IngestionJobStatus.Failed)]
    [InlineData(IngestionJobStatus.Cancelled)]
    [InlineData(IngestionJobStatus.PartiallyCompleted)]
    public async Task DeleteJobAsync_ShouldMarkQueuedAndTerminalJobsAsDeleting(IngestionJobStatus status) {
        // UNIT-LEVEL ASSUMPTION: this test stubs TrySetDeletingAsync to return true for every
        // status (see the constructor setup), so it only proves the SERVICE guard lets each
        // status through to the atomic transition. The contract that the REPOSITORY actually
        // accepts Queued (and the terminal statuses) via its [Status] IN (...) clause is an
        // integration-level property — see IngestionJobRepository.TrySetDeletingAsync's
        // deletableStatuses list, which must mirror the service's deletable set. If the
        // repository ever rejects one of these statuses, DeleteJobAsync will throw
        // DeleteJobError.ConcurrentModification here (the constructor stub returns true, so a
        // passing run still implies the service did not reject the status up front).
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = status,
                InputType = IngestionJobType.StructuredSpecification,
                InputRef = $"upload-{status}"
            });

        // Per-status explicit stub: the constructor default already returns true, but making it
        // explicit per-job keeps the assertion "TrySetDeletingAsync returned true" legible. If
        // this ever returns false, DeleteJobAsync throws and this Theory case fails.
        _repository
            .Setup(r => r.TrySetDeletingAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut();

        await sut.DeleteJobAsync(jobId, TestUserId, CancellationToken.None);

        // For every deletable status (including Queued), the service must delegate to the
        // atomic transition and must never fall back to the non-atomic UpdateAsync path.
        _repository.Verify(
            r => r.TrySetDeletingAsync(jobId, It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteJobAsync_WhenJobIsFinished_ShouldTransitionToDeletingWithoutDeletingRow() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Completed,
                InputType = IngestionJobType.StructuredSpecification,
                InputRef = "upload-finished-001"
            });

        var sut = CreateSut();

        await sut.DeleteJobAsync(jobId, TestUserId, CancellationToken.None);

        _repository.Verify(
            r => r.TrySetDeletingAsync(jobId, It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteJobAsync_ShouldNotRemoveAssociatedAssetsDirectly() {
        // DeleteJobAsync now only performs the status transition; asset teardown is owned
        // by ExecuteJobCleanupAsync (background service). Verify none of the chunk/artifact/
        // blob/search/graph cleanup paths are touched on the HTTP delete path.
        var jobId = Guid.NewGuid();
        var uploadId = Guid.NewGuid().ToString();

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Completed,
                InputType = IngestionJobType.PDFManual,
                InputRef = uploadId,
                ExpectedChunkCount = 2
            });

        var sut = CreateSut();

        await sut.DeleteJobAsync(jobId, TestUserId, CancellationToken.None);

        _searchDocumentService.Verify(
            s => s.DeleteDocumentsAsync(It.IsAny<IEnumerable<string>>()),
            Times.Never);
        _chunkRepository.Verify(r => r.DeleteByArtifactIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _chunkRepository.Verify(r => r.DeleteByIngestionJobIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _chunkRepository.Verify(r => r.DeleteByUploadIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _artifactRepository.Verify(r => r.DeleteByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _blobStorage.Verify(s => s.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _graphRepository.Verify(r => r.DeleteByDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        _repository.Verify(
            r => r.TrySetDeletingAsync(jobId, It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteJobAsync_WhenJobIsAlreadyDeleting_ShouldRejectWithIdempotencyGuard() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Deleting,
                InputType = IngestionJobType.PDFManual,
                InputRef = "upload-deleting-001"
            });

        var sut = CreateSut();

        var act = async () => await sut.DeleteJobAsync(jobId, TestUserId, CancellationToken.None);

        await act.Should().ThrowAsync<DeleteJobException>()
            .Where(ex => ex.Error == DeleteJobError.AlreadyDeleting)
            .WithMessage($"*'{jobId}'*already being deleted*");
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.TrySetDeletingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteJobAsync_WhenTrySetDeletingReturnsFalse_ShouldThrow() {
        // The atomic UPDATE returned 0 affected rows — the job was either not found or
        // not in a deletable terminal state (e.g. a concurrent request already flipped it).
        // The service must surface this as a concurrency error and must not mutate the row
        // via the non-atomic UpdateAsync path.
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Completed,
                InputType = IngestionJobType.PDFManual,
                InputRef = "upload-concurrent-001"
            });
        _repository
            .Setup(r => r.TrySetDeletingAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();

        var act = async () => await sut.DeleteJobAsync(jobId, TestUserId, CancellationToken.None);

        await act.Should().ThrowAsync<DeleteJobException>()
            .Where(ex => ex.Error == DeleteJobError.ConcurrentModification)
            .WithMessage("*could not be deleted*modified concurrently*");
        _repository.Verify(r => r.TrySetDeletingAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()), Times.Never);
    }

    #region ExecuteJobCleanupAsync (background service)

    [Fact]
    public async Task ExecuteJobCleanupAsync_WhenJobNotFound_LogsWarningAndReturns() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);

        var sut = CreateSut();

        // A missing job means a prior cleanup pass already removed it — treat as success.
        var act = async () => await sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _repository.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteJobCleanupAsync_WhenSuccessful_DeletesAllAssetsAndJobRow() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Deleting,
                InputType = IngestionJobType.PDFManual,
                InputRef = "upload-cleanup-success"
            });
        _repository
            .Setup(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut();

        await sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        _repository.Verify(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);
        // No rollback on success — the row is removed by DeleteAsync, never UpdateAsync'd.
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteJobCleanupAsync_WhenFinalDeleteThrows_RollsBackToFailed() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Deleting,
                InputType = IngestionJobType.PDFManual,
                InputRef = "upload-cleanup-throw"
            });
        _repository
            .Setup(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB connection lost"));

        var sut = CreateSut();

        // The original exception must rethrow so the background service observes the failure.
        await Assert.ThrowsAsync<Exception>(
            () => sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None));

        // The catch block rolls the row back to Failed so it resurfaces for operator attention.
        _repository.Verify(
            r => r.UpdateAsync(
                It.Is<IngestionJob>(j =>
                    j.Status == IngestionJobStatus.Failed
                    && (j.FailureReason ?? string.Empty).Contains("Background cleanup failed")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteJobCleanupAsync_WhenAssetCleanupThrows_RollsBackToFailed() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Deleting,
                InputType = IngestionJobType.PDFManual,
                InputRef = "upload-cleanup-asset-throw"
            });

        // GetDeleteArtifactsAsync is the first read inside DeleteAssociatedAssetsAsync and is
        // NOT wrapped in RunBestEffortAsync, so a throw here propagates to the outer catch.
        // This re-setup overrides the constructor's empty-array default (last setup wins).
        _artifactRepository
            .Setup(r => r.GetByIngestionJobIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("artifact store unavailable"));

        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None));

        // Asset cleanup failed before the final row delete was reached.
        _repository.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(
            r => r.UpdateAsync(
                It.Is<IngestionJob>(j =>
                    j.Status == IngestionJobStatus.Failed
                    && (j.FailureReason ?? string.Empty).Contains("Background cleanup failed")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteJobCleanupAsync_WhenDeleteAsyncReturnsFalse_LogsWarningButDoesNotThrow() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Deleting,
                InputType = IngestionJobType.PDFManual,
                InputRef = "upload-cleanup-already-gone"
            });
        _repository
            .Setup(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();

        var act = async () => await sut.ExecuteJobCleanupAsync(jobId, CancellationToken.None);

        // A missing row during the final delete is a benign race, not an error.
        await act.Should().NotThrowAsync();
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    [Fact]
    public async Task ClearFinishedJobsAsync_ShouldRemoveFinishedRows() {
        var firstJobId = Guid.NewGuid();
        var secondJobId = Guid.NewGuid();

        _repository
            .Setup(r => r.GetByStatusesAsync(
                It.Is<IReadOnlyCollection<IngestionJobStatus>>(statuses =>
                    statuses.Contains(IngestionJobStatus.Completed)
                    && statuses.Contains(IngestionJobStatus.PartiallyCompleted)
                    && statuses.Count == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new IngestionJob {
                    IngestionJobId = firstJobId,
                    Status = IngestionJobStatus.Completed,
                    InputRef = "upload-finished-002"
                },
                new IngestionJob {
                    IngestionJobId = secondJobId,
                    Status = IngestionJobStatus.PartiallyCompleted,
                    InputRef = "upload-finished-002"
                }
            ]);

        _repository
            .Setup(r => r.DeleteByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Count == 2
                    && ids.Contains(firstJobId)
                    && ids.Contains(secondJobId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var sut = CreateSut();

        var deletedCount = await sut.ClearFinishedJobsAsync(TestUserId, CancellationToken.None);

        deletedCount.Should().Be(2);
        _repository.Verify(
            r => r.DeleteByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RetryJobAsync_WhenBikeGraphJobFailed_ShouldRejectLocalFirstRetry() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Failed,
                InputType = IngestionJobType.BikeGraph,
                InputRef = "upload-graph-retry-001"
            });

        var sut = CreateSut();

        var act = async () => await sut.RetryJobAsync(jobId, TestUserId, CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*re-queueing the original local source file*");
        _graphEntityIngestionService.Verify(g => g.IngestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.CreateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RetryJobAsync_WhenStructuredSpecificationJobFailed_ShouldRejectLocalFirstRetry() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Cancelled,
                InputType = IngestionJobType.StructuredSpecification,
                InputRef = "upload-retry-002"
            });

        var sut = CreateSut();

        var act = async () => await sut.RetryJobAsync(jobId, TestUserId, CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*re-queueing the original local source file*");
        _repository.Verify(r => r.CreateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RetryJobAsync_WhenPdfManualJobFailedAndBlobExists_ShouldResetJobToQueued() {
        var jobId = Guid.NewGuid();
        const string uploadId = "upload-retry-pdf-001";
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Failed,
                InputType = IngestionJobType.PDFManual,
                InputRef = uploadId,
                FailureReason = "Pipeline failed during chunking",
                ErrorMessage = "Something went wrong",
                ErrorsJson = "{\"error\":\"detail\"}",
                CurrentStage = "chunking",
                MetadataJson = "{\"make\":\"Honda\"}"
            });
        _blobStorage
            .Setup(b => b.ExistsAsync("raw-uploads", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut();

        var response = await sut.RetryJobAsync(jobId, TestUserId, CancellationToken.None);

        response.Should().NotBeNull();
        response.Status.Should().Be(IngestionJobStatus.Queued.ToString());
        _blobStorage.Verify(
            b => b.ExistsAsync("raw-uploads", It.Is<string>(p => p == $"{uploadId}/source.pdf"), It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(
            r => r.UpdateAsync(
                It.Is<IngestionJob>(j =>
                    j.Status == IngestionJobStatus.Queued
                    && j.FailureReason == null
                    && j.ErrorMessage == null
                    && j.ErrorsJson == null
                    && j.CurrentStage == null
                    && j.MetadataJson == null
                    && j.StartedAtUtc == null
                    && j.CompletedAtUtc == null
                    && j.StageSetAtUtc == null
                    && j.ExpectedChunkCount == null
                    && j.IndexedChunkCount == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RetryJobAsync_WhenPdfManualJobFailedAndBlobMissing_ShouldThrowInvalidOperationException() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Failed,
                InputType = IngestionJobType.PDFManual,
                InputRef = "upload-retry-missing"
            });
        _blobStorage
            .Setup(b => b.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();

        var act = async () => await sut.RetryJobAsync(jobId, TestUserId, CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*was not found in blob storage*");
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RetryJobAsync_WhenPdfManualJobAwaitingMetadata_ShouldResetJobToQueued() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.AwaitingMetadata,
                InputType = IngestionJobType.PDFManual,
                InputRef = "upload-retry-awaits",
                CurrentStage = "needs-manual-metadata"
            });
        _blobStorage
            .Setup(b => b.ExistsAsync("raw-uploads", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut();

        var response = await sut.RetryJobAsync(jobId, TestUserId, CancellationToken.None);

        response.Should().NotBeNull();
        response.Status.Should().Be(IngestionJobStatus.Queued.ToString());
        _repository.Verify(
            r => r.UpdateAsync(
                It.Is<IngestionJob>(j => j.Status == IngestionJobStatus.Queued),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportGraphArtifactsAsync_WhenArtifactsAreMissing_ShouldThrowAndNotIngest() {
        _blobStorage
            .Setup(b => b.ExistsAsync("raw-uploads", "graph-entities/upload-missing-graph/entities.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();

        var act = () => sut.ImportGraphArtifactsAsync(
            new GraphImportStartRequest { UploadId = "upload-missing-graph" },
            TestUserId,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*graph-entities/upload-missing-graph/entities.json*");
        _graphEntityIngestionService.Verify(g => g.IngestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #region Manual PDF end-to-end

    [Fact]
    public async Task ManualPdfJob_StartToCompletion_FullLifecycle() {
        var jobId = Guid.NewGuid();
        const string uploadId = "upload-manual-001";
        const string runId = "run-manual-001";

        _repository
            .Setup(r => r.CreateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob job, CancellationToken _) => {
                job.IngestionJobId = jobId;
                return job;
            });

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Processing,
                InputType = IngestionJobType.PDFManual,
                InputRef = uploadId,
                DocIngestionRunId = runId
            });

        var sut = CreateSut();

        var startResponse = await sut.StartJobAsync(
            new IngestionJobStartRequest { UploadId = uploadId, DocumentType = "manual-pdf", ProcessorRunId = runId },
            TestUserId);

        startResponse.Status.Should().Be(IngestionJobStatus.Queued.ToString());
        startResponse.DocIngestionRunId.Should().Be(runId);
        startResponse.FailureReason.Should().BeNull();

        var statusResponse = await sut.GetJobStatusAsync(jobId, TestUserId);

        statusResponse.Should().NotBeNull();
        statusResponse!.Status.Should().Be(IngestionJobStatus.Processing.ToString());
        statusResponse.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task GetJobStatusAsync_WhenFailedWithGenericReason_ShouldReturnPersistedFailure() {
        var jobId = Guid.NewGuid();
        const string runId = "processor-run-001";
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Failed,
            InputType = IngestionJobType.PDFManual,
            InputRef = "upload-xyz",
            DocIngestionRunId = runId,
            FailureReason = "Pipeline reported status 'failed'.",
        };

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var sut = CreateSut();

        var response = await sut.GetJobStatusAsync(jobId, TestUserId);

        response.Should().NotBeNull();
        response!.FailureReason.Should().Be("Pipeline reported status 'failed'.");
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Deleting status guards (T3)

    [Fact]
    public async Task CancelJobAsync_WhenJobIsDeleting_LogsWarningAndReturnsWithoutError() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Deleting,
                InputType = IngestionJobType.PDFManual,
                InputRef = "upload-deleting-cancel"
            });

        var sut = CreateSut();

        await sut.CancelJobAsync(jobId, TestUserId, CancellationToken.None);

        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FailJobAsync_WhenJobIsDeleting_LogsWarningAndReturnsWithoutError() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Deleting,
                InputType = IngestionJobType.PDFManual,
                InputRef = "upload-deleting-fail"
            });

        var sut = CreateSut();

        await sut.FailJobAsync(jobId, "processor reported failure", TestUserId, CancellationToken.None);

        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TransitionStageAsync_WhenJobIsDeleting_ReturnsCurrentResponseWithoutModifying() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Deleting,
                InputType = IngestionJobType.PDFManual,
                InputRef = "upload-deleting-transition",
                CurrentStage = "deleting"
            });

        var sut = CreateSut();

        var response = await sut.TransitionStageAsync(
            jobId,
            new IngestionJobStageRequest { Stage = "completed" },
            CancellationToken.None);

        response.Should().NotBeNull();
        response.Status.Should().Be(IngestionJobStatus.Deleting.ToString());
        _repository.Verify(r => r.UpdateStageAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPendingStorageFilesAsync_WhenOnlyJobIsDeleting_ExcludesFile() {
        _blobStorage
            .Setup(b => b.ListAsync("raw-uploads", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BlobObjectDescriptor {
                    Name = "upload-deleting-only/source.pdf",
                    LastModifiedUtc = DateTimeOffset.UtcNow
                }
            ]);

        var deletingJob = new IngestionJob {
            IngestionJobId = Guid.NewGuid(),
            InputRef = "upload-deleting-only",
            InputType = IngestionJobType.PDFManual,
            Status = IngestionJobStatus.Deleting
        };

        // GetPendingStorageFilesAsync batches the (InputRef, InputType) lookup; returning
        // the Deleting job means HasPendingWorkflow rejects it and the file is excluded.
        _repository
            .Setup(r => r.GetLatestByInputRefsAsync(
                It.IsAny<IReadOnlyCollection<(string, IngestionJobType)>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { deletingJob });

        var sut = CreateSut();

        var result = await sut.GetPendingStorageFilesAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentIngestionJobsAsync_ShouldReturnDeletingJobsForUiVisibility() {
        var deletingJob = new IngestionJob {
            IngestionJobId = Guid.NewGuid(),
            Status = IngestionJobStatus.Deleting,
            InputType = IngestionJobType.PDFManual,
            InputRef = "upload-deleting-recent"
        };
        _repository
            .Setup(r => r.GetRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([deletingJob]);

        var sut = CreateSut();

        var result = await sut.GetRecentIngestionJobsAsync(50, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Status.Should().Be(IngestionJobStatus.Deleting.ToString());
    }


    #region TransitionStageAsync "completed" branch (T8)

    /// <summary>
    /// T8 acceptance #1: a non-terminal job parked in <c>Indexing</c> by the fire-and-forget
    /// Python <c>report_stage("completed")</c> callback must not be left stuck in
    /// <c>Indexing</c>. When the "completed" stage report carries no failure reason the
    /// job transitions to <c>Completed</c> (the synchronous indexing outcome is owned by
    /// the controller via <c>TryTransitionSearchChunkJobToTerminalAsync</c>, which is a
    /// no-op once the job is already terminal).
    /// </summary>
    [Fact]
    public async Task TransitionStageAsync_CompletedWithoutFailure_OnNonTerminalIndexingJob_TransitionsToCompleted() {
        var jobId = Guid.NewGuid();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Indexing,
            InputType = IngestionJobType.PDFManual,
            InputRef = "upload-completed-no-failure",
            CurrentStage = "indexing",
            ExpectedChunkCount = 12,
            IndexedChunkCount = 0
        };
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _repository
            .Setup(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<IngestionJob, CancellationToken>((j, _) => {
                // Reflect the mutation performed by the sut so the returned response
                // carries the new status, mirroring the production EF/Dapper behavior.
                job.Status = j.Status;
                job.CurrentStage = j.CurrentStage;
                job.StageSetAtUtc = j.StageSetAtUtc;
                job.CompletedAtUtc = j.CompletedAtUtc;
                job.FailureReason = j.FailureReason;
            });

        var sut = CreateSut();

        var response = await sut.TransitionStageAsync(
            jobId,
            new IngestionJobStageRequest { Stage = "completed" },
            CancellationToken.None);

        response.Should().NotBeNull();
        response.Status.Should().Be(IngestionJobStatus.Completed.ToString());
        response.CurrentStage.Should().Be("completed");
        response.FailureReason.Should().BeNull();
        job.Status.Should().Be(IngestionJobStatus.Completed);
        job.CompletedAtUtc.Should().NotBeNull();
        job.FailureReason.Should().BeNull();
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// T8 acceptance #2: a non-terminal job whose "completed" stage report carries a
    /// failure reason transitions to <c>Failed</c> rather than being re-stamped as
    /// <c>Indexing</c> and left parked.
    /// </summary>
    [Fact]
    public async Task TransitionStageAsync_CompletedWithFailure_OnNonTerminalIndexingJob_TransitionsToFailed() {
        var jobId = Guid.NewGuid();
        const string failureReason = "Python pipeline reported completion with errors";
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Indexing,
            InputType = IngestionJobType.PDFManual,
            InputRef = "upload-completed-with-failure",
            CurrentStage = "indexing",
            ExpectedChunkCount = 12,
            IndexedChunkCount = 0
        };
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _repository
            .Setup(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<IngestionJob, CancellationToken>((j, _) => {
                job.Status = j.Status;
                job.CurrentStage = j.CurrentStage;
                job.StageSetAtUtc = j.StageSetAtUtc;
                job.FailureReason = j.FailureReason;
                job.ErrorsJson = j.ErrorsJson;
                job.ErrorMessage = j.ErrorMessage;
            });

        var sut = CreateSut();

        var response = await sut.TransitionStageAsync(
            jobId,
            new IngestionJobStageRequest { Stage = "completed", FailureReason = failureReason },
            CancellationToken.None);

        response.Should().NotBeNull();
        response.Status.Should().Be(IngestionJobStatus.Failed.ToString());
        response.CurrentStage.Should().Be("completed");
        response.FailureReason.Should().Be(failureReason);
        job.Status.Should().Be(IngestionJobStatus.Failed);
        job.FailureReason.Should().Be(failureReason);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TransitionStageAsync_FailedStage_OnNonTerminalJob_TransitionsToFailed() {
        var jobId = Guid.NewGuid();
        const string failureReason = "Processor reported failure during chunking";
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Processing,
            InputType = IngestionJobType.PDFManual,
            InputRef = "upload-failed-stage",
            CurrentStage = "chunking",
            ExpectedChunkCount = 12,
            IndexedChunkCount = 1
        };
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _repository
            .Setup(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<IngestionJob, CancellationToken>((j, _) => {
                job.Status = j.Status;
                job.CurrentStage = j.CurrentStage;
                job.StageSetAtUtc = j.StageSetAtUtc;
                job.CompletedAtUtc = j.CompletedAtUtc;
                job.FailureReason = j.FailureReason;
                job.ErrorsJson = j.ErrorsJson;
                job.ErrorMessage = j.ErrorMessage;
            });

        var sut = CreateSut();

        var response = await sut.TransitionStageAsync(
            jobId,
            new IngestionJobStageRequest { Stage = "failed", FailureReason = failureReason, ChunksProcessed = 1, TotalChunks = 12 },
            CancellationToken.None);

        response.Status.Should().Be(IngestionJobStatus.Failed.ToString());
        response.CurrentStage.Should().Be("failed");
        response.FailureReason.Should().Be(failureReason);
        job.Status.Should().Be(IngestionJobStatus.Failed);
        job.CurrentStage.Should().Be("failed");
        job.CompletedAtUtc.Should().NotBeNull();
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TransitionStageAsync_NonTerminalStageWithFailureReason_OnNonTerminalJob_TransitionsToFailed() {
        var jobId = Guid.NewGuid();
        const string failureReason = "Expected 1536 dims, got 2560";
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Processing,
            InputType = IngestionJobType.PDFManual,
            InputRef = "upload-failure-reason-compat",
            CurrentStage = "chunking",
            ComputeProvider = "LocalProcessor"
        };
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _repository
            .Setup(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<IngestionJob, CancellationToken>((j, _) => {
                job.Status = j.Status;
                job.CurrentStage = j.CurrentStage;
                job.StageSetAtUtc = j.StageSetAtUtc;
                job.CompletedAtUtc = j.CompletedAtUtc;
                job.FailureReason = j.FailureReason;
                job.ErrorsJson = j.ErrorsJson;
                job.ErrorMessage = j.ErrorMessage;
            });

        var sut = CreateSut();

        var response = await sut.TransitionStageAsync(
            jobId,
            new IngestionJobStageRequest { Stage = "chunking", FailureReason = failureReason },
            CancellationToken.None);

        response.Status.Should().Be(IngestionJobStatus.Failed.ToString());
        response.CurrentStage.Should().Be("failed");
        response.FailureReason.Should().Be(failureReason);
        job.Status.Should().Be(IngestionJobStatus.Failed);
        job.CurrentStage.Should().Be("failed");
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// T8 acceptance #3 / regression guard for the terminal-status guard: a job that has
    /// already reached <c>Completed</c> must not be flipped by a late or replayed
    /// <c>report_stage("completed")</c> callback. The same invariant holds for the other
    /// terminal statuses (<c>Failed</c>, <c>Cancelled</c>, <c>PartiallyCompleted</c>,
    /// <c>Deleting</c>); the existing <c>WhenJobIsDeleting_*</c> test covers the
    /// <c>Deleting</c> case.
    /// </summary>
    [Fact]
    public async Task TransitionStageAsync_Completed_OnAlreadyTerminalCompletedJob_DoesNotFlipStatus() {
        var jobId = Guid.NewGuid();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Completed,
            InputType = IngestionJobType.PDFManual,
            InputRef = "upload-already-completed",
            CurrentStage = "completed",
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var sut = CreateSut();

        var response = await sut.TransitionStageAsync(
            jobId,
            new IngestionJobStageRequest { Stage = "completed", FailureReason = "late replay" },
            CancellationToken.None);

        response.Should().NotBeNull();
        response.Status.Should().Be(IngestionJobStatus.Completed.ToString());
        response.FailureReason.Should().BeNull();
        job.Status.Should().Be(IngestionJobStatus.Completed);
        _repository.Verify(r => r.UpdateStageAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }


    #endregion

    #endregion

    private IngestionJobService CreateSut() {
        return new IngestionJobService(
            _repository.Object,
            _blobStorage.Object,
            _artifactRepository.Object,
            _chunkRepository.Object,
            _searchDocumentService.Object,
            _graphRepository.Object,
            _graphEntityIngestionService.Object,
            _graphIngestionChannel,
            CreateBlobOptions(),
            CreateIngestionOptions(),
            _logger.Object);
    }

    private static IOptions<BlobStorageOptions> CreateBlobOptions() =>
        Options.Create(new BlobStorageOptions {
            RawUploadsContainer = "raw-uploads"
        });

    private static IOptions<IngestionOptions> CreateIngestionOptions() =>
        Options.Create(new IngestionOptions {
            Mode = ProcessingMode.Local,
            PdfPipelineId = "pdf-pipeline-id",
            CsvPipelineId = "csv-pipeline-id"
        });
}
