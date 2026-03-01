using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Pipeline;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.UnitTests.Pipeline;

/// <summary>
/// Unit tests for <see cref="IngestionJobService"/> dual-mode branching.
/// Verifies that <see cref="ProcessingMode.Local"/> routes to the local pipeline
/// and <see cref="ProcessingMode.Fabric"/> routes to the Fabric pipeline.
/// </summary>
public sealed class IngestionJobServiceDualModeTests {
    private readonly Mock<IIngestionJobRepository> _repository;
    private readonly Mock<IFabricPipelineService> _fabricPipeline;
    private readonly Mock<ILocalPipelineService> _localPipeline;
    private readonly Mock<ILogger<IngestionJobService>> _logger;

    private const string TestUserId = "test-user-oid";
    private const string TestRunId = "run-12345";

    public IngestionJobServiceDualModeTests() {
        _repository = new Mock<IIngestionJobRepository>();
        _fabricPipeline = new Mock<IFabricPipelineService>();
        _localPipeline = new Mock<ILocalPipelineService>();
        _logger = new Mock<ILogger<IngestionJobService>>();

        // Default repository setup: CreateAsync returns the entity with an ID.
        _repository
            .Setup(r => r.CreateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob job, CancellationToken _) => job);

        // Default repository setup: UpdateAsync completes without error.
        _repository
            .Setup(r => r.UpdateAsync(It.IsAny<IngestionJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    #region Constructor null checks

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenRepositoryIsNull() {
        var opts = Options.Create(new IngestionOptions());
        var act = () => new IngestionJobService(
            null!, _fabricPipeline.Object, _localPipeline.Object, opts, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("repository");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenFabricPipelineIsNull() {
        var opts = Options.Create(new IngestionOptions());
        var act = () => new IngestionJobService(
            _repository.Object, null!, _localPipeline.Object, opts, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("fabricPipeline");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLocalPipelineIsNull() {
        var opts = Options.Create(new IngestionOptions());
        var act = () => new IngestionJobService(
            _repository.Object, _fabricPipeline.Object, null!, opts, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("localPipeline");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsIsNull() {
        var act = () => new IngestionJobService(
            _repository.Object, _fabricPipeline.Object, _localPipeline.Object, null!, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull() {
        var opts = Options.Create(new IngestionOptions());
        var act = () => new IngestionJobService(
            _repository.Object, _fabricPipeline.Object, _localPipeline.Object, opts, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    #endregion

    #region Dual-mode branching: StartJobAsync

    [Fact]
    public async Task ProcessingMode_Local_ShouldCallLocalPipeline() {
        // Arrange
        _localPipeline
            .Setup(p => p.TriggerPipelineAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestRunId);

        var sut = CreateSut(ProcessingMode.Local);
        var request = new IngestionJobStartRequest {
            UploadId = "upload-abc",
            DocumentType = "manual-pdf"
        };

        // Act
        var response = await sut.StartJobAsync(request, TestUserId);

        // Assert
        response.Should().NotBeNull();
        response.FabricRunId.Should().Be(TestRunId);
        response.Status.Should().Be(IngestionJobStatus.Processing.ToString());

        _localPipeline.Verify(
            p => p.TriggerPipelineAsync("upload-abc", "manual-pdf", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _fabricPipeline.Verify(
            p => p.TriggerPipelineAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessingMode_Fabric_ShouldCallFabricPipeline() {
        // Arrange
        _fabricPipeline
            .Setup(p => p.TriggerPipelineAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestRunId);

        var sut = CreateSut(ProcessingMode.Fabric);
        var request = new IngestionJobStartRequest {
            UploadId = "upload-xyz",
            DocumentType = "spec-dataset"
        };

        // Act
        var response = await sut.StartJobAsync(request, TestUserId);

        // Assert
        response.Should().NotBeNull();
        response.FabricRunId.Should().Be(TestRunId);
        response.Status.Should().Be(IngestionJobStatus.Processing.ToString());

        _fabricPipeline.Verify(
            p => p.TriggerPipelineAsync("upload-xyz", "spec-dataset", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _localPipeline.Verify(
            p => p.TriggerPipelineAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region GetJobStatusAsync

    [Fact]
    public async Task GetJobStatus_Local_ShouldReturnStatusFromRepository() {
        // Arrange — repository returns a completed job.
        var jobId = Guid.NewGuid();
        var job = new IngestionJob {
            IngestionJobId = jobId,
            Status = IngestionJobStatus.Completed,
            InputType = IngestionJobType.PDFManual,
            InputRef = "upload-abc"
        };

        _repository
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var sut = CreateSut(ProcessingMode.Local);

        // Act
        var response = await sut.GetJobStatusAsync(jobId, TestUserId);

        // Assert
        response.Should().NotBeNull();
        response!.Status.Should().Be(IngestionJobStatus.Completed.ToString());
        response.JobId.Should().Be(jobId);
    }

    #endregion

    #region Helpers

    private IngestionJobService CreateSut(ProcessingMode mode) {
        var opts = Options.Create(new IngestionOptions {
            Mode = mode,
            PdfPipelineId = "pdf-pipeline-id",
            CsvPipelineId = "csv-pipeline-id"
        });

        return new IngestionJobService(
            _repository.Object,
            _fabricPipeline.Object,
            _localPipeline.Object,
            opts,
            _logger.Object);
    }

    #endregion
}
