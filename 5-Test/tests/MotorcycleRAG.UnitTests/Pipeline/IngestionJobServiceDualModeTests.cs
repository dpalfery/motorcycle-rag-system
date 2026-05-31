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
/// Verifies job creation plus refresh of local-processing statuses into persisted history.
/// </summary>
public sealed class IngestionJobServiceDualModeTests {
    private const string TestUserId = "test-user-oid";
    private const string TestRunId = "run-12345";

    private readonly Mock<IIngestionJobRepository> _repository;
    private readonly Mock<IBlobStorageService> _blobStorage;
    private readonly Mock<ILocalPipelineService> _pipelineService;
    private readonly Mock<IGraphEntityIngestionService> _graphEntityIngestionService;
    private readonly Mock<ILogger<IngestionJobService>> _logger;

    public IngestionJobServiceDualModeTests() {
        _repository = new Mock<IIngestionJobRepository>();
        _blobStorage = new Mock<IBlobStorageService>();
        _pipelineService = new Mock<ILocalPipelineService>();
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
            _pipelineService.Object,
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
            _pipelineService.Object,
            _graphEntityIngestionService.Object,
            CreateBlobOptions(),
            CreateIngestionOptions(),
            _logger.Object);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("blobStorageService");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenPipelineServiceIsNull() {
        var sut = () => new IngestionJobService(
            _repository.Object,
            _blobStorage.Object,
            null!,
            _graphEntityIngestionService.Object,
            CreateBlobOptions(),
            CreateIngestionOptions(),
            _logger.Object);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("pipelineService");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenGraphEntityIngestionServiceIsNull() {
        var sut = () => new IngestionJobService(
            _repository.Object,
            _blobStorage.Object,
            _pipelineService.Object,
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
            _pipelineService.Object,
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
            _pipelineService.Object,
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
            _pipelineService.Object,
            _graphEntityIngestionService.Object,
            CreateBlobOptions(),
            CreateIngestionOptions(),
            null!);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task StartJobAsync_ShouldTriggerPipelineAndReturnProcessingResponse() {
        _pipelineService
            .Setup(p => p.TriggerPipelineAsync(
                "upload-abc",
                "manual-pdf",
                "pdf-pipeline-id",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestRunId);

        var sut = CreateSut();
        var request = new IngestionJobStartRequest {
            UploadId = "upload-abc",
            DocumentType = "manual-pdf"
        };

        var response = await sut.StartJobAsync(request, TestUserId);

        response.DocIngestionRunId.Should().Be(TestRunId);
        response.Status.Should().Be(IngestionJobStatus.Processing.ToString());
        _pipelineService.VerifyAll();
        _repository.Verify(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetJobStatusAsync_ShouldRefreshActiveJobStatusFromPipelineService() {
        var jobId = Guid.NewGuid();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Processing,
            InputType = IngestionJobType.StructuredSpecification,
            InputRef = "upload-abc",
            DocIngestionRunId = TestRunId
        };

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _pipelineService
            .Setup(p => p.GetRunStatusAsync(TestRunId, "csv-pipeline-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync("completed");

        var sut = CreateSut();

        var response = await sut.GetJobStatusAsync(jobId, TestUserId);

        response.Should().NotBeNull();
        response!.Status.Should().Be(IngestionJobStatus.Completed.ToString());
        response.CompletedAtUtc.Should().NotBeNull();
        _repository.Verify(r => r.UpdateAsync(It.Is<IngestionJob>(j =>
            j.IngestionJobId == jobId &&
            j.Status == IngestionJobStatus.Completed &&
            j.CompletedAtUtc.HasValue), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetJobStatusAsync_BikeGraphCompletion_ShouldImportGraphEntitiesBeforeCompleting() {
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

        _pipelineService
            .Setup(p => p.GetRunStatusAsync(TestRunId, string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync("completed");

        _blobStorage
            .Setup(b => b.ExistsAsync("raw-uploads", "graph-entities/upload-graph-001/entities.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut();

        var response = await sut.GetJobStatusAsync(jobId, TestUserId);

        response.Should().NotBeNull();
        response!.Status.Should().Be(IngestionJobStatus.Completed.ToString());
        _graphEntityIngestionService.Verify(g => g.IngestAsync("upload-graph-001", It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task DeletePendingStorageFileAsync_ShouldDeleteArtifactsAndAssociatedHistory() {
        _repository
            .Setup(r => r.DeleteByInputRefAsync("upload-pending-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var sut = CreateSut();

        await sut.DeletePendingStorageFileAsync("upload-pending-001", "spec-dataset", CancellationToken.None);

        _blobStorage.Verify(
            b => b.DeleteIfExistsAsync("raw-uploads", "upload-pending-001/source.pdf", It.IsAny<CancellationToken>()),
            Times.Once);
        _blobStorage.Verify(
            b => b.DeleteIfExistsAsync("raw-uploads", "upload-pending-001.csv", It.IsAny<CancellationToken>()),
            Times.Once);
        _blobStorage.Verify(
            b => b.DeleteIfExistsAsync("raw-uploads", "graph-entities/upload-pending-001/entities.json", It.IsAny<CancellationToken>()),
            Times.Once);
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
    public async Task GetPendingStorageFilesAsync_WhenGraphOnlyWorkflowCompleted_ShouldNotReturnPendingUpload() {
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

        result.Should().BeEmpty();
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
    public async Task DeleteJobAsync_WhenJobIsFinished_ShouldDeleteArtifactsBeforeRemovingHistoryRow() {
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

        _blobStorage.Verify(
            b => b.DeleteIfExistsAsync("raw-uploads", "upload-finished-001/source.pdf", It.IsAny<CancellationToken>()),
            Times.Once);
        _blobStorage.Verify(
            b => b.DeleteIfExistsAsync("raw-uploads", "upload-finished-001.csv", It.IsAny<CancellationToken>()),
            Times.Once);
        _blobStorage.Verify(
            b => b.DeleteIfExistsAsync("raw-uploads", "graph-entities/upload-finished-001/entities.json", It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(r => r.DeleteAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClearFinishedJobsAsync_ShouldDeleteArtifactsOncePerUploadAndRemoveFinishedRows() {
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
        _blobStorage.Verify(
            b => b.DeleteIfExistsAsync("raw-uploads", "upload-finished-002/source.pdf", It.IsAny<CancellationToken>()),
            Times.Once);
        _blobStorage.Verify(
            b => b.DeleteIfExistsAsync("raw-uploads", "upload-finished-002.csv", It.IsAny<CancellationToken>()),
            Times.Once);
        _blobStorage.Verify(
            b => b.DeleteIfExistsAsync("raw-uploads", "graph-entities/upload-finished-002/entities.json", It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(
            r => r.DeleteByStatusesAsync(It.IsAny<IReadOnlyCollection<IngestionJobStatus>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RetryJobAsync_WhenBikeGraphJobFailed_ShouldUseGraphImportFlow() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Failed,
                InputType = IngestionJobType.BikeGraph,
                InputRef = "upload-graph-retry-001"
            });

        _blobStorage
            .Setup(b => b.ExistsAsync("raw-uploads", "graph-entities/upload-graph-retry-001/entities.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _graphEntityIngestionService
            .Setup(g => g.IngestAsync("upload-graph-retry-001", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();

        var response = await sut.RetryJobAsync(jobId, TestUserId, CancellationToken.None);

        response.InputType.Should().Be(IngestionJobType.BikeGraph.ToString());
        response.InputRef.Should().Be("upload-graph-retry-001");
        _pipelineService.Verify(
            p => p.TriggerPipelineAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(
            r => r.CreateAsync(It.Is<IngestionJob>(job =>
                job.InputType == IngestionJobType.BikeGraph
                && job.InputRef == "upload-graph-retry-001"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RetryJobAsync_WhenStructuredSpecificationJobFailed_ShouldUseMappedStartJobFlow() {
        var jobId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Cancelled,
                InputType = IngestionJobType.StructuredSpecification,
                InputRef = "upload-retry-002"
            });

        _pipelineService
            .Setup(p => p.TriggerPipelineAsync(
                "upload-retry-002",
                "spec-dataset",
                "csv-pipeline-id",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestRunId);

        var sut = CreateSut();

        var response = await sut.RetryJobAsync(jobId, TestUserId, CancellationToken.None);

        response.Status.Should().Be(IngestionJobStatus.Processing.ToString());
        response.InputType.Should().Be(IngestionJobType.StructuredSpecification.ToString());
        _pipelineService.VerifyAll();
    }

    [Fact]
    public async Task ImportGraphArtifactsAsync_WhenArtifactsAreMissing_ShouldNotCreateHistoryRow() {
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
        _repository.Verify(r => r.CreateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
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

        _pipelineService
            .Setup(p => p.TriggerPipelineAsync(uploadId, "manual-pdf", "pdf-pipeline-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(runId);

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJob {
                IngestionJobId = jobId,
                Status = IngestionJobStatus.Processing,
                InputType = IngestionJobType.PDFManual,
                InputRef = uploadId,
                DocIngestionRunId = runId
            });

        _pipelineService
            .Setup(p => p.GetRunStatusAsync(runId, "pdf-pipeline-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync("completed");

        var sut = CreateSut();

        var startResponse = await sut.StartJobAsync(
            new IngestionJobStartRequest { UploadId = uploadId, DocumentType = "manual-pdf" },
            TestUserId);

        startResponse.Status.Should().Be(IngestionJobStatus.Processing.ToString());
        startResponse.DocIngestionRunId.Should().Be(runId);
        startResponse.FailureReason.Should().BeNull();
        _pipelineService.Verify(
            p => p.TriggerPipelineAsync(uploadId, "manual-pdf", "pdf-pipeline-id", It.IsAny<CancellationToken>()),
            Times.Once);

        var statusResponse = await sut.GetJobStatusAsync(jobId, TestUserId);

        statusResponse.Should().NotBeNull();
        statusResponse!.Status.Should().Be(IngestionJobStatus.Completed.ToString());
        statusResponse.CompletedAtUtc.Should().NotBeNull();
        statusResponse.FailureReason.Should().BeNull();
        _repository.Verify(
            r => r.UpdateAsync(
                It.Is<IngestionJob>(j => j.IngestionJobId == jobId && j.Status == IngestionJobStatus.Completed),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task StartJobAsync_WhenLocalProcessorReturnsHttpError_FailureReasonIncludesExceptionMessage() {
        // LocalPipelineService throws this exception when the processor returns HTTP 503.
        // The failure reason should remain generic so internal exception details are not exposed.
        _pipelineService
            .Setup(p => p.TriggerPipelineAsync(
                It.IsAny<string>(), "manual-pdf", "pdf-pipeline-id", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Local pipeline trigger returned HTTP 503 for document type 'manual-pdf'."));

        _repository
            .Setup(r => r.UpdateStatusAsync(
                It.IsAny<Guid>(), IngestionJobStatus.Failed, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();

        var response = await sut.StartJobAsync(
            new IngestionJobStartRequest { UploadId = "upload-fail-001", DocumentType = "manual-pdf" },
            TestUserId);

        response.Status.Should().Be(IngestionJobStatus.Failed.ToString());
        response.FailureReason.Should().Be("Failed to trigger pipeline.");
        _repository.Verify(
            r => r.UpdateStatusAsync(
                It.IsAny<Guid>(), IngestionJobStatus.Failed, "Failed to trigger pipeline.", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartJobAsync_WhenLocalProcessorIsUnreachable_ShouldMarkJobFailed() {
        // Simulates the local Python processor not being started (connection refused / network error).
        _pipelineService
            .Setup(p => p.TriggerPipelineAsync(
                It.IsAny<string>(), "manual-pdf", "pdf-pipeline-id", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(
                "No connection could be made because the target machine actively refused it."));

        _repository
            .Setup(r => r.UpdateStatusAsync(
                It.IsAny<Guid>(), IngestionJobStatus.Failed, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();

        var response = await sut.StartJobAsync(
            new IngestionJobStartRequest { UploadId = "upload-fail-002", DocumentType = "manual-pdf" },
            TestUserId);

        response.Status.Should().Be(IngestionJobStatus.Failed.ToString());
        response.FailureReason.Should().NotBeNullOrEmpty();
    }

    #endregion

    private IngestionJobService CreateSut() {
        return new IngestionJobService(
            _repository.Object,
            _blobStorage.Object,
            _pipelineService.Object,
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
