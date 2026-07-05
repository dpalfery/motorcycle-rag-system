using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Azure;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
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
    private readonly Mock<IGraphEntityIngestionService> _graphEntityIngestionService;
    private readonly Mock<ILogger<IngestionJobService>> _logger;

    public IngestionJobServiceDualModeTests() {
        _repository = new Mock<IIngestionJobRepository>();
        _blobStorage = new Mock<IBlobStorageService>();
        _graphEntityIngestionService = new Mock<IGraphEntityIngestionService>();
        _logger = new Mock<ILogger<IngestionJobService>>();

        _repository
            .Setup(r => r.CreateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob job, CancellationToken _) => job);

        _repository
            .Setup(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenRepositoryIsNull() {
        var sut = () => new IngestionJobService(
            null!,
            _blobStorage.Object,
            _graphEntityIngestionService.Object,
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
            _graphEntityIngestionService.Object,
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
            null!,
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
            _graphEntityIngestionService.Object,
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
            _graphEntityIngestionService.Object,
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
            _graphEntityIngestionService.Object,
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

        _repository
            .Setup(r => r.GetLatestByInputAsync("upload-pending-101", IngestionJobType.StructuredSpecification, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);
        _repository
            .Setup(r => r.GetLatestByInputAsync("upload-pending-101", IngestionJobType.BikeGraph, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);
        _repository
            .Setup(r => r.GetLatestByInputAsync("upload-pending-202", IngestionJobType.PDFManual, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);

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

        _repository
            .Setup(r => r.GetLatestByInputAsync("upload-graph-complete-001", IngestionJobType.StructuredSpecification, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);

        _repository
            .Setup(r => r.GetLatestByInputAsync("upload-graph-complete-001", IngestionJobType.BikeGraph, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = Guid.NewGuid(),
                InputRef = "upload-graph-complete-001",
                InputType = IngestionJobType.BikeGraph,
                Status = IngestionJobStatus.Completed
            });

        var sut = CreateSut();

        var result = await sut.GetPendingStorageFilesAsync(CancellationToken.None);

        result.Should().ContainSingle();
        result[0].UploadId.Should().Be("upload-graph-complete-001");
        result[0].GraphImportStatus.Should().Be("Completed");
    }

    [Fact]
    public async Task DeleteJobAsync_WhenJobIsNotTerminal_ShouldThrowInvalidOperationException() {
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

        var act = () => sut.DeleteJobAsync(jobId, TestUserId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Only terminal ingestion jobs can be deleted.");
        _repository.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteJobAsync_WhenJobIsFinished_ShouldDeleteHistoryRow() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Completed,
                InputType = IngestionJobType.StructuredSpecification,
                InputRef = "upload-finished-001"
            });

        _repository
            .Setup(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut();

        await sut.DeleteJobAsync(jobId, TestUserId, CancellationToken.None);

        _repository.Verify(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);
    }

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

    private IngestionJobService CreateSut() {
        return new IngestionJobService(
            _repository.Object,
            _blobStorage.Object,
            _graphEntityIngestionService.Object,
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
