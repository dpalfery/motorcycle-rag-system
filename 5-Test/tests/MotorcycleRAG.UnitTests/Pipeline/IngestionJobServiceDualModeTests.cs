using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Azure;
using MotorcycleRAG.Application.Pipeline;
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

        response.FabricRunId.Should().Be(TestRunId);
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
            FabricRunId = TestRunId
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
            FabricRunId = TestRunId
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
