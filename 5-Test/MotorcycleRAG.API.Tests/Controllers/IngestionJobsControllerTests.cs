using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Application.Features.Ingestion.Validators;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Presentation.API.Controllers;

public sealed class IngestionJobsControllerTests
{
    [Fact]
    public async Task UploadAsync_WithBikeGraphCsv_UploadsCsvToRawUploadsAndReturnsAccepted()
    {
        // Arrange
        var blobStorage = new Mock<IBlobStorageService>();
        blobStorage
            .Setup(service => service.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example/raw-uploads/upload.csv");

        var sut = CreateController(blobStorage.Object);
        await using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes("make,model\nYamaha,R1"));
        var file = CreateFormFile(csvStream, "bikes.csv", "text/csv");

        // Act
        var result = await sut.UploadAsync(file, "bike-graph", CancellationToken.None);

        // Assert
        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        var response = accepted.Value.Should().BeOfType<IngestionUploadResponse>().Subject;

        response.UploadId.Should().NotBeNullOrWhiteSpace();
        response.FileName.Should().Be("bikes.csv");
        response.DocumentType.Should().Be("bike-graph");
        response.Status.Should().Be("uploaded");

        blobStorage.Verify(service => service.UploadAsync(
            "raw-uploads",
            $"{response.UploadId}.csv",
            It.IsAny<Stream>(),
            "text/csv",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsync_WithBikeGraphNonCsv_ReturnsBadRequest()
    {
        // Arrange
        var blobStorage = new Mock<IBlobStorageService>();
        var sut = CreateController(blobStorage.Object);
        await using var pdfStream = new MemoryStream("%PDF-1.7"u8.ToArray());
        var file = CreateFormFile(pdfStream, "bikes.pdf", "application/pdf");

        // Act
        var result = await sut.UploadAsync(file, "bike-graph", CancellationToken.None);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Detail.Should().Contain(".csv");

        blobStorage.Verify(service => service.UploadAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void GetUploadConstraints_ReturnsBlobBackedIngestionLimits()
    {
        var sut = CreateController(Mock.Of<IBlobStorageService>());

        var result = sut.GetUploadConstraints();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var constraints = ok.Value.Should().BeOfType<FileUploadConstraints>().Subject;
        constraints.MaxFileSizeBytes.Should().Be(2_000_000_000L);
        constraints.MaxFilesPerBatch.Should().Be(1);
        constraints.SupportedFileTypes.Should().Contain(["manual-pdf", "spec-dataset", "bike-graph"]);
        constraints.SupportedExtensions.Should().Contain([".pdf", ".csv"]);
    }

    [Fact]
    public async Task UploadAsync_WithMixedCaseDocumentType_ReturnsNormalizedDocumentType()
    {
        var blobStorage = new Mock<IBlobStorageService>();
        blobStorage
            .Setup(service => service.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example/raw-uploads/source.pdf");

        var sut = CreateController(blobStorage.Object);
        await using var stream = new MemoryStream("%PDF-1.7"u8.ToArray());
        var file = CreateFormFile(stream, "manual.pdf", "application/pdf");

        var result = await sut.UploadAsync(file, "Manual-Pdf", CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        var response = accepted.Value.Should().BeOfType<IngestionUploadResponse>().Subject;
        response.DocumentType.Should().Be("manual-pdf");

        blobStorage.Verify(service => service.UploadAsync(
            "raw-uploads",
            $"{response.UploadId}/source.pdf",
            It.IsAny<Stream>(),
            "application/pdf",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsync_WhenFileExceedsConfiguredLimit_ReturnsBadRequest()
    {
        var blobStorage = new Mock<IBlobStorageService>();
        var sut = CreateController(blobStorage.Object, ingestionOptions: new IngestionOptions
        {
            MaxInputBytes = 4
        });
        await using var stream = new MemoryStream("too-large"u8.ToArray());
        var file = CreateFormFile(stream, "manual.pdf", "application/pdf");

        var result = await sut.UploadAsync(file, "manual-pdf", CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeOfType<ProblemDetails>()
            .Which.Title.Should().Be("File too large");
        blobStorage.Verify(service => service.UploadAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void UploadAsync_DoesNotOverrideConfiguredHostMultipartLimit()
    {
        var method = typeof(IngestionJobsController).GetMethod(nameof(IngestionJobsController.UploadAsync));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(RequestFormLimitsAttribute), inherit: false)
            .Should()
            .BeEmpty();
        method.GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: false)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void UploadAsync_DisablesAntiforgeryForAuthenticatedMultipartApiUpload()
    {
        var method = typeof(IngestionJobsController).GetMethod(nameof(IngestionJobsController.UploadAsync));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(IgnoreAntiforgeryTokenAttribute), inherit: false)
            .Should()
            .ContainSingle();
        method.GetCustomAttributes(typeof(RequireAntiforgeryTokenAttribute), inherit: false)
            .Should()
            .ContainSingle()
            .Which
            .As<RequireAntiforgeryTokenAttribute>()
            .RequiresValidation.Should().BeFalse();
        method.GetCustomAttributes(typeof(ConsumesAttribute), inherit: false)
            .Should()
            .ContainSingle()
            .Which
            .As<ConsumesAttribute>()
            .ContentTypes.Should().ContainSingle("multipart/form-data");
    }

    [Fact]
    public async Task StartJobAsync_WhenServiceThrowsInvalidOperation_ReturnsInternalServerError()
    {
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.StartJobAsync(
                It.IsAny<IngestionJobStartRequest>(),
                "test-user",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Failed to create ingestion job"));

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);
        var request = new IngestionJobStartRequest
        {
            UploadId = Guid.NewGuid().ToString(),
            DocumentType = "manual-pdf",
            ProcessorRunId = Guid.NewGuid().ToString("N"),
            Configuration = new IngestionJobConfiguration { ExtractGraphRelationships = false, OcrEnabled = false }
        };

        var result = await sut.StartJobAsync(request, CancellationToken.None);

        var statusCode = result.Should().BeOfType<ObjectResult>().Subject;
        statusCode.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var problem = statusCode.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Failed to start ingestion job");
        problem.Detail.Should().Be("The ingestion job could not be created.");
        problem.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.Detail.Should().NotContain("Failed to create ingestion job");
    }

    [Fact]
    public async Task StartJobAsync_WithoutProcessorRunId_ReturnsBadRequest()
    {
        var ingestionJobs = new Mock<IIngestionJobService>();
        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);
        var request = new IngestionJobStartRequest
        {
            UploadId = Guid.NewGuid().ToString(),
            DocumentType = "manual-pdf",
            ProcessorRunId = string.Empty,
            Configuration = new IngestionJobConfiguration { ExtractGraphRelationships = false, OcrEnabled = false }
        };

        var result = await sut.StartJobAsync(request, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Detail.Should().Contain("ProcessorRunId is required.");
        ingestionJobs.Verify(
            service => service.StartJobAsync(It.IsAny<IngestionJobStartRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportGraphAsync_WithUploadId_ReturnsAccepted()
    {
        var ingestionJobs = new Mock<IIngestionJobService>();
        var expected = new IngestionJobStatusResponse {
            JobId = Guid.NewGuid(),
            Status = "Completed",
            InputType = "BikeGraph",
            InputRef = "upload-123",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        ingestionJobs
            .Setup(service => service.ImportGraphArtifactsAsync(
                It.Is<GraphImportStartRequest>(request => request.UploadId == "upload-123"),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.ImportGraphAsync(new GraphImportStartRequest { UploadId = "upload-123" }, CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        accepted.Value.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task GetRecentJobsAsync_WhenServiceThrowsInvalidOperation_ReturnsInternalServerError()
    {
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.GetRecentIngestionJobsAsync(25, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.GetRecentJobsAsync(25, CancellationToken.None);

        var statusCode = result.Result.Should().BeOfType<ObjectResult>().Subject;
        statusCode.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        statusCode.Value.Should().BeOfType<ProblemDetails>()
            .Which.Title.Should().Be("Failed to load ingestion jobs");
    }

    [Fact]
    public async Task GetRecentJobsAsync_WhenServiceReturnsJobs_ReturnsOk()
    {
        var expected = new[]
        {
            new IngestionJobStatusResponse
            {
                JobId = Guid.NewGuid(),
                Status = "Completed",
                InputType = "ManualPdf",
                InputRef = "upload-123",
                CreatedAtUtc = DateTimeOffset.UtcNow
            }
        };
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.GetRecentIngestionJobsAsync(25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.GetRecentJobsAsync(25, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task GetPendingFilesAsync_WhenServiceThrowsInvalidOperation_ReturnsInternalServerError()
    {
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.GetPendingStorageFilesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.GetPendingFilesAsync(CancellationToken.None);

        var statusCode = result.Result.Should().BeOfType<ObjectResult>().Subject;
        statusCode.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        statusCode.Value.Should().BeOfType<ProblemDetails>()
            .Which.Title.Should().Be("Failed to load pending storage files");
    }

    [Fact]
    public async Task GetPendingFilesAsync_WhenServiceReturnsFiles_ReturnsOk()
    {
        var expected = new[]
        {
            new PendingStorageFileDto
            {
                UploadId = "upload-123",
                BlobName = "raw-uploads/upload-123/source.pdf",
                DocumentType = "manual-pdf",
                SizeBytes = 42,
                LastModifiedUtc = DateTimeOffset.UtcNow
            }
        };
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.GetPendingStorageFilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.GetPendingFilesAsync(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task ClearPendingFilesAsync_ReturnsDeletedCountResponse() {
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.ClearPendingStorageFilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.ClearPendingFilesAsync(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new IngestionCleanupResponse {
            Scope = "pending-files",
            DeletedCount = 2
        });
    }

    [Fact]
    public async Task DeleteJobAsync_WhenJobIsDeletable_ReturnsAcceptedWithDeletingStatus() {
        var jobId = Guid.NewGuid();
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.DeleteJobAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.DeleteJobAsync(jobId, CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        accepted.Value.Should().BeEquivalentTo(new { jobId, status = "Deleting" });
        ingestionJobs.Verify(
            service => service.DeleteJobAsync(jobId, "test-user", It.IsAny<CancellationToken>()),
            Times.Once);
        // No pre-fetch: the controller must not perform the second DB read via GetJobStatusAsync.
        ingestionJobs.Verify(
            service => service.GetJobStatusAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteJobAsync_WhenJobIsActive_ReturnsAccepted() {
        var jobId = Guid.NewGuid();
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.DeleteJobAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.DeleteJobAsync(jobId, CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        accepted.Value.Should().BeEquivalentTo(new { jobId, status = "Deleting" });
        ingestionJobs.Verify(
            service => service.GetJobStatusAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteJobAsync_WhenJobNotFound_ReturnsNotFound() {
        var jobId = Guid.NewGuid();
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.DeleteJobAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DeleteJobException(
                DeleteJobError.NotFound,
                $"Ingestion job '{jobId}' not found."));

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.DeleteJobAsync(jobId, CancellationToken.None);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var problem = notFound.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status404NotFound);
        problem.Detail.Should().Contain("not found");
        ingestionJobs.Verify(
            service => service.GetJobStatusAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteJobAsync_WhenJobAlreadyDeleting_ReturnsConflictWithDistinctDetail() {
        var jobId = Guid.NewGuid();
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.DeleteJobAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DeleteJobException(
                DeleteJobError.AlreadyDeleting,
                $"Ingestion job '{jobId}' is already being deleted."));

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.DeleteJobAsync(jobId, CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status409Conflict);
        problem.Title.Should().Be("Job deletion already in progress");
        problem.Detail.Should().Contain("already being deleted");
        ingestionJobs.Verify(
            service => service.GetJobStatusAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteJobAsync_WhenConcurrentModification_ReturnsConflict() {
        var jobId = Guid.NewGuid();
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.DeleteJobAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DeleteJobException(
                DeleteJobError.ConcurrentModification,
                "The job could not be deleted. It may have been modified concurrently."));

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.DeleteJobAsync(jobId, CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status409Conflict);
        problem.Title.Should().Be("Job deletion rejected");
        problem.Detail.Should().Contain("modified concurrently");
        ingestionJobs.Verify(
            service => service.GetJobStatusAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteJobAsync_WhenPlainInvalidOperationException_FallsBackToStringMapping() {
        // Backward-compatibility safety net: a service path still throwing a plain
        // InvalidOperationException must map via the message-string fallback.
        var jobId = Guid.NewGuid();
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.DeleteJobAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"Ingestion job '{jobId}' not found."));

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.DeleteJobAsync(jobId, CancellationToken.None);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var problem = notFound.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status404NotFound);
        problem.Detail.Should().Contain("not found");
    }

    [Fact]
    public async Task CancelJobAsync_WhenJobExists_ReturnsNoContent() {
        var jobId = Guid.NewGuid();
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.GetJobStatusAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJobStatusResponse {
                JobId = jobId,
                Status = "Processing",
                InputType = "StructuredSpecification",
                InputRef = "upload-123"
            });
        ingestionJobs
            .Setup(service => service.CancelJobAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.CancelJobAsync(jobId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        ingestionJobs.Verify(
            service => service.CancelJobAsync(jobId, "test-user", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RetryJobAsync_WhenJobIsFailed_ReturnsAccepted() {
        var jobId = Guid.NewGuid();
        var existing = new IngestionJobStatusResponse {
            JobId = jobId,
            Status = "Failed",
            InputType = "BikeGraph",
            InputRef = "upload-graph-123"
        };
        var retried = new IngestionJobStatusResponse {
            JobId = Guid.NewGuid(),
            Status = "Processing",
            InputType = "BikeGraph",
            InputRef = "upload-graph-123"
        };

        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.GetJobStatusAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        ingestionJobs
            .Setup(service => service.RetryJobAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(retried);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.RetryJobAsync(jobId, CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        accepted.Value.Should().BeEquivalentTo(retried);
    }

    [Fact]
    public async Task ClearFinishedJobsAsync_ReturnsDeletedCountResponse() {
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(service => service.ClearFinishedJobsAsync("test-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.ClearFinishedJobsAsync(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new IngestionCleanupResponse {
            Scope = "finished-jobs",
            DeletedCount = 3
        });
    }

    [Fact]
    public async Task DeletePendingFileAsync_WithoutDocumentType_StillDeletesUpload() {
        var ingestionJobs = new Mock<IIngestionJobService>();
        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.DeletePendingFileAsync("upload-123", null, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        ingestionJobs.Verify(
            service => service.DeletePendingStorageFileAsync("upload-123", string.Empty, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // === POST /api/ingestion/jobs/{jobId}/metadata ===

    [Fact]
    public async Task SubmitManualMetadataAsync_HappyPath_ReturnsOkWithJobStatus() {
        var jobId = Guid.NewGuid();
        var metadataJson = """{"make":"Honda","model":"CBR600RR","year":2023,"category":"sport"}""";
        var expected = new IngestionJobStatusResponse { JobId = jobId, Status = "Processing" };

        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(s => s.SubmitManualMetadataAsync(jobId, metadataJson, "test-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.SubmitManualMetadataAsync(jobId, new ManualMetadataSubmitRequest { MetadataJson = metadataJson }, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(expected);

        ingestionJobs.Verify(
            s => s.SubmitManualMetadataAsync(jobId, metadataJson, "test-user", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SubmitManualMetadataAsync_NullBody_ReturnsBadRequest() {
        var sut = CreateController(Mock.Of<IBlobStorageService>());

        var result = await sut.SubmitManualMetadataAsync(Guid.NewGuid(), null, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SubmitManualMetadataAsync_EmptyMetadataJson_ReturnsBadRequest() {
        var sut = CreateController(Mock.Of<IBlobStorageService>());

        var result = await sut.SubmitManualMetadataAsync(
            Guid.NewGuid(),
            new ManualMetadataSubmitRequest { MetadataJson = "   " },
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SubmitManualMetadataAsync_JobNotFound_ReturnsNotFound() {
        // H1: KeyNotFoundException → 404 (not InvalidOperationException → 500).
        var jobId = Guid.NewGuid();
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(s => s.SubmitManualMetadataAsync(jobId, It.IsAny<string>(), "test-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException($"Ingestion job '{jobId}' not found."));

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.SubmitManualMetadataAsync(
            jobId,
            new ManualMetadataSubmitRequest { MetadataJson = """{"make":"a","model":"b","year":1,"category":"c"}""" },
            CancellationToken.None);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var problem = notFound.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task SubmitManualMetadataAsync_InvalidJson_ReturnsBadRequest() {
        var jobId = Guid.NewGuid();
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(s => s.SubmitManualMetadataAsync(jobId, It.IsAny<string>(), "test-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("metadataJson is not valid JSON."));

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.SubmitManualMetadataAsync(
            jobId,
            new ManualMetadataSubmitRequest { MetadataJson = "{bad" },
            CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SubmitManualMetadataAsync_DbFailure_ThrowsInvalidOperationException_NotMappedTo404() {
        // H1: DB failures (InvalidOperationException) must NOT map to 404 — they propagate as 500.
        var jobId = Guid.NewGuid();
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(s => s.SubmitManualMetadataAsync(jobId, It.IsAny<string>(), "test-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Failed to update metadata"));

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var act = () => sut.SubmitManualMetadataAsync(
            jobId,
            new ManualMetadataSubmitRequest { MetadataJson = """{"make":"a","model":"b","year":1,"category":"c"}""" },
            CancellationToken.None);

        // InvalidOperationException is NOT caught by the controller — it propagates to the
        // global exception handler which returns 500. This is the H1 fix.
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // === GET /api/ingestion/jobs/{jobId}/metadata ===

    [Fact]
    public async Task GetJobMetadataAsync_HappyPath_ReturnsOkWithMetadata() {
        var jobId = Guid.NewGuid();
        var expected = new IngestionJobMetadataResponse {
            JobId = jobId,
            Make = "Honda",
            Model = "CBR600RR",
            Year = 2023,
            Category = "sport",
            IsComplete = true,
            FillRate = 1.0
        };

        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(s => s.GetJobMetadataAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.GetJobMetadataAsync(jobId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task GetJobMetadataAsync_JobNotFound_ReturnsNotFound() {
        var jobId = Guid.NewGuid();
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(s => s.GetJobMetadataAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJobMetadataResponse?)null);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.GetJobMetadataAsync(jobId, CancellationToken.None);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var problem = notFound.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task GetJobMetadataAsync_DbFailure_ReturnsInternalServerError() {
        // DB failures (InvalidOperationException) must surface as 500, not 404.
        var jobId = Guid.NewGuid();
        var ingestionJobs = new Mock<IIngestionJobService>();
        ingestionJobs
            .Setup(s => s.GetJobMetadataAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB connection failed"));

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.GetJobMetadataAsync(jobId, CancellationToken.None);

        var serverError = result.Should().BeOfType<ObjectResult>().Subject;
        serverError.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    // === POST /api/ingestion/jobs/{jobId}/reprocess ===

    [Fact]
    public async Task ReprocessJobAsync_WhenServiceSucceeds_ReturnsAcceptedWithResult()
    {
        var jobId = Guid.NewGuid();
        var expected = new ReprocessResultDto(
            ArtifactsProcessed: 3,
            ArtifactsSucceeded: 2,
            ArtifactsPartiallyIndexed: 1,
            ArtifactsFailed: 0);

        var reprocessService = new Mock<IChunkReprocessService>();
        reprocessService
            .Setup(service => service.ReprocessByJobIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), reprocessService: reprocessService.Object);

        var result = await sut.ReprocessJobAsync(jobId, CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        accepted.Value.Should().BeEquivalentTo(expected);
        reprocessService.Verify(service => service.ReprocessByJobIdAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReprocessJobAsync_WhenServiceThrows_ReturnsInternalServerError()
    {
        var jobId = Guid.NewGuid();
        var reprocessService = new Mock<IChunkReprocessService>();
        reprocessService
            .Setup(service => service.ReprocessByJobIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("job not found"));

        var sut = CreateController(Mock.Of<IBlobStorageService>(), reprocessService: reprocessService.Object);

        var result = await sut.ReprocessJobAsync(jobId, CancellationToken.None);

        var statusCode = result.Should().BeOfType<ObjectResult>().Subject;
        statusCode.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var problem = statusCode.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Reprocess operation failed");
        problem.Detail.Should().Be("The reprocess operation could not be completed.");
        problem.Detail.Should().NotContain("job not found");
    }

    // === POST /api/ingestion/jobs/reprocess/all ===

    [Fact]
    public async Task ReprocessAllAsync_WhenServiceSucceeds_ReturnsAcceptedWithResult()
    {
        var expected = new ReprocessResultDto(
            ArtifactsProcessed: 10,
            ArtifactsSucceeded: 8,
            ArtifactsPartiallyIndexed: 1,
            ArtifactsFailed: 1);

        var reprocessService = new Mock<IChunkReprocessService>();
        reprocessService
            .Setup(service => service.ReprocessAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), reprocessService: reprocessService.Object);

        var result = await sut.ReprocessAllAsync(CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        accepted.Value.Should().BeEquivalentTo(expected);
        reprocessService.Verify(service => service.ReprocessAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReprocessAllAsync_WhenServiceThrows_ReturnsInternalServerError()
    {
        var reprocessService = new Mock<IChunkReprocessService>();
        reprocessService
            .Setup(service => service.ReprocessAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));

        var sut = CreateController(Mock.Of<IBlobStorageService>(), reprocessService: reprocessService.Object);

        var result = await sut.ReprocessAllAsync(CancellationToken.None);

        var statusCode = result.Should().BeOfType<ObjectResult>().Subject;
        statusCode.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var problem = statusCode.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Reprocess operation failed");
        problem.Detail.Should().Be("The reprocess operation could not be completed.");
    }

    // === POST /api/ingestion/jobs/reprocess/not-succeeded ===

    [Fact]
    public async Task ReprocessNotSucceededAsync_WhenServiceSucceeds_ReturnsAcceptedWithResult()
    {
        var expected = new ReprocessResultDto(
            ArtifactsProcessed: 5,
            ArtifactsSucceeded: 3,
            ArtifactsPartiallyIndexed: 0,
            ArtifactsFailed: 2);

        var reprocessService = new Mock<IChunkReprocessService>();
        reprocessService
            .Setup(service => service.ReprocessAllNotSucceededAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var sut = CreateController(Mock.Of<IBlobStorageService>(), reprocessService: reprocessService.Object);

        var result = await sut.ReprocessNotSucceededAsync(CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        accepted.Value.Should().BeEquivalentTo(expected);
        reprocessService.Verify(service => service.ReprocessAllNotSucceededAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReprocessNotSucceededAsync_WhenServiceThrows_ReturnsInternalServerError()
    {
        var reprocessService = new Mock<IChunkReprocessService>();
        reprocessService
            .Setup(service => service.ReprocessAllNotSucceededAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));

        var sut = CreateController(Mock.Of<IBlobStorageService>(), reprocessService: reprocessService.Object);

        var result = await sut.ReprocessNotSucceededAsync(CancellationToken.None);

        var statusCode = result.Should().BeOfType<ObjectResult>().Subject;
        statusCode.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var problem = statusCode.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Reprocess operation failed");
        problem.Detail.Should().Be("The reprocess operation could not be completed.");
    }

    [Fact]
    public async Task UploadAsync_WithOctetStreamPdf_AssumesApplicationPdfContentType()
    {
        // Covers GetContentType's fallback for manual-pdf when the client sent application/octet-stream.
        var blobStorage = new Mock<IBlobStorageService>();
        blobStorage
            .Setup(service => service.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example/raw-uploads/source.pdf");

        var sut = CreateController(blobStorage.Object);
        await using var stream = new MemoryStream("%PDF-1.7"u8.ToArray());
        var file = CreateFormFile(stream, "manual.pdf", "application/octet-stream");

        await sut.UploadAsync(file, "manual-pdf", CancellationToken.None);

        blobStorage.Verify(service => service.UploadAsync(
            "raw-uploads",
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            "application/pdf",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsync_WithOctetStreamCsv_AssumesTextCsvContentType()
    {
        // Covers GetContentType's fallback for CSV document types when the client sent application/octet-stream.
        var blobStorage = new Mock<IBlobStorageService>();
        blobStorage
            .Setup(service => service.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example/raw-uploads/upload.csv");

        var sut = CreateController(blobStorage.Object);
        await using var stream = new MemoryStream("make,model\nHonda,CBR"u8.ToArray());
        var file = CreateFormFile(stream, "bikes.csv", string.Empty);

        await sut.UploadAsync(file, "bike-graph", CancellationToken.None);

        blobStorage.Verify(service => service.UploadAsync(
            "raw-uploads",
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            "text/csv",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task GetRecentJobsAsync_WhenTopIsOutsideAllowedRange_ReturnsBadRequest(int top)
    {
        var result = await CreateController(Mock.Of<IBlobStorageService>())
            .GetRecentJobsAsync(top, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UploadAsync_WhenFileIsMissingOrEmpty_ReturnsBadRequest()
    {
        var sut = CreateController(Mock.Of<IBlobStorageService>());
        var missing = await sut.UploadAsync(null, "manual-pdf", CancellationToken.None);
        await using var emptyStream = new MemoryStream();
        var empty = await sut.UploadAsync(CreateFormFile(emptyStream, "manual.pdf", "application/pdf"), "manual-pdf", CancellationToken.None);

        missing.Should().BeOfType<BadRequestObjectResult>();
        empty.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UploadAsync_WhenDocumentTypeIsInvalidOrExtensionDoesNotMatch_ReturnsBadRequest()
    {
        var sut = CreateController(Mock.Of<IBlobStorageService>());
        await using var csv = new MemoryStream("make,model"u8.ToArray());
        await using var pdf = new MemoryStream("%PDF"u8.ToArray());

        var invalidType = await sut.UploadAsync(CreateFormFile(csv, "bikes.csv", "text/csv"), "unknown", CancellationToken.None);
        var invalidExtension = await sut.UploadAsync(CreateFormFile(pdf, "manual.pdf", "application/pdf"), "spec-dataset", CancellationToken.None);

        invalidType.Should().BeOfType<BadRequestObjectResult>();
        invalidExtension.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UploadAsync_WhenStorageThrows_ReturnsSanitizedServerError()
    {
        var storage = new Mock<IBlobStorageService>();
        storage.Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage failure"));
        await using var stream = new MemoryStream("make,model"u8.ToArray());

        var result = await CreateController(storage.Object)
            .UploadAsync(CreateFormFile(stream, "bikes.csv", "text/csv"), "spec-dataset", CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task StartJobAsync_WhenBodyMissingOrValid_MapsBadRequestAndAccepted()
    {
        var service = new Mock<IIngestionJobService>();
        var request = CreateValidStartRequest();
        var expected = new IngestionJobStatusResponse { JobId = Guid.NewGuid(), Status = "Queued" };
        service.Setup(x => x.StartJobAsync(request, "test-user", It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var sut = CreateController(Mock.Of<IBlobStorageService>(), service.Object);

        (await sut.StartJobAsync(null, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
        var accepted = await sut.StartJobAsync(request, CancellationToken.None);
        accepted.Should().BeOfType<AcceptedResult>().Which.Value.Should().Be(expected);
    }

    [Fact]
    public async Task ImportGraphAsync_WhenRequestIsMissingOrUploadIsBlank_ReturnsBadRequest()
    {
        var sut = CreateController(Mock.Of<IBlobStorageService>());

        (await sut.ImportGraphAsync(null, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
        (await sut.ImportGraphAsync(new GraphImportStartRequest { UploadId = " " }, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DeletePendingFileAsync_WhenUploadIdIsBlank_ReturnsBadRequest()
    {
        var result = await CreateController(Mock.Of<IBlobStorageService>())
            .DeletePendingFileAsync(" ", "manual-pdf", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetJobStatusAsync_MapsNotFoundAndSuccess()
    {
        var jobId = Guid.NewGuid();
        var service = new Mock<IIngestionJobService>();
        service.Setup(x => x.GetJobStatusAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJobStatusResponse?)null);
        var sut = CreateController(Mock.Of<IBlobStorageService>(), service.Object);

        (await sut.GetJobStatusAsync(jobId, CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();
        var expected = new IngestionJobStatusResponse { JobId = jobId, Status = "Processing" };
        service.Setup(x => x.GetJobStatusAsync(jobId, "test-user", It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        (await sut.GetJobStatusAsync(jobId, CancellationToken.None)).Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(expected);
    }

    [Fact]
    public async Task ClearFailedJobsAsync_UsesAuthenticatedUser()
    {
        var service = new Mock<IIngestionJobService>();
        service.Setup(x => x.ClearFailedJobsAsync("test-user", It.IsAny<CancellationToken>())).ReturnsAsync(4);

        var result = await CreateController(Mock.Of<IBlobStorageService>(), service.Object)
            .ClearFailedJobsAsync(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeEquivalentTo(new IngestionCleanupResponse { Scope = "failed-jobs", DeletedCount = 4 });
    }

    [Fact]
    public async Task CancelAndFailJobAsync_WhenJobIsMissing_ReturnNotFound()
    {
        var jobId = Guid.NewGuid();
        var service = new Mock<IIngestionJobService>();
        service.Setup(x => x.GetJobStatusAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJobStatusResponse?)null);
        var sut = CreateController(Mock.Of<IBlobStorageService>(), service.Object);

        (await sut.CancelJobAsync(jobId, CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();
        (await sut.FailJobAsync(jobId, null!, CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task FailJobAsync_WhenJobExists_UsesDefaultReasonAndReturnsNoContent()
    {
        var jobId = Guid.NewGuid();
        var service = new Mock<IIngestionJobService>();
        service.Setup(x => x.GetJobStatusAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJobStatusResponse { JobId = jobId, Status = "Processing" });
        var sut = CreateController(Mock.Of<IBlobStorageService>(), service.Object);

        var result = await sut.FailJobAsync(jobId, null!, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        service.Verify(x => x.FailJobAsync(jobId, "Marked as failed.", "test-user", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryJobAsync_MapsMissingNonFailedAndServiceRejection()
    {
        var jobId = Guid.NewGuid();
        var service = new Mock<IIngestionJobService>();
        service.Setup(x => x.GetJobStatusAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJobStatusResponse?)null);
        var sut = CreateController(Mock.Of<IBlobStorageService>(), service.Object);
        (await sut.RetryJobAsync(jobId, CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();

        service.Setup(x => x.GetJobStatusAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJobStatusResponse { JobId = jobId, Status = "Completed" });
        (await sut.RetryJobAsync(jobId, CancellationToken.None)).Should().BeOfType<ConflictObjectResult>();

        service.Setup(x => x.GetJobStatusAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJobStatusResponse { JobId = jobId, Status = "Cancelled" });
        service.Setup(x => x.RetryJobAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("retry rejected"));
        (await sut.RetryJobAsync(jobId, CancellationToken.None)).Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public void Constructor_RejectsEachRequiredDependency()
    {
        var service = Mock.Of<IIngestionJobService>();
        var validator = new IngestionJobValidator();
        var storage = Mock.Of<IBlobStorageService>();
        var blobOptions = Options.Create(new BlobStorageOptions());
        var ingestionOptions = Options.Create(new IngestionOptions());
        var reprocess = Mock.Of<IChunkReprocessService>();
        var logger = NullLogger<IngestionJobsController>.Instance;

        ((Action)(() => new IngestionJobsController(null!, validator, storage, blobOptions, ingestionOptions, reprocess, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new IngestionJobsController(service, null!, storage, blobOptions, ingestionOptions, reprocess, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new IngestionJobsController(service, validator, null!, blobOptions, ingestionOptions, reprocess, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new IngestionJobsController(service, validator, storage, null!, ingestionOptions, reprocess, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new IngestionJobsController(service, validator, storage, blobOptions, null!, reprocess, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new IngestionJobsController(service, validator, storage, blobOptions, ingestionOptions, null!, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new IngestionJobsController(service, validator, storage, blobOptions, ingestionOptions, reprocess, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Actions_WithoutSubjectClaim_UseUnknownUser()
    {
        var jobId = Guid.NewGuid();
        var start = CreateValidStartRequest();
        var graph = new GraphImportStartRequest { UploadId = "upload" };
        var metadata = new ManualMetadataSubmitRequest { MetadataJson = "{}" };
        var status = new IngestionJobStatusResponse { JobId = jobId, Status = "Cancelled" };
        var service = new Mock<IIngestionJobService>();
        service.Setup(x => x.StartJobAsync(start, "unknown", It.IsAny<CancellationToken>())).ReturnsAsync(status);
        service.Setup(x => x.ImportGraphArtifactsAsync(graph, "unknown", It.IsAny<CancellationToken>())).ReturnsAsync(status);
        service.Setup(x => x.GetJobStatusAsync(jobId, "unknown", It.IsAny<CancellationToken>())).ReturnsAsync(status);
        service.Setup(x => x.SubmitManualMetadataAsync(jobId, metadata.MetadataJson, "unknown", It.IsAny<CancellationToken>())).ReturnsAsync(status);
        service.Setup(x => x.GetJobMetadataAsync(jobId, "unknown", It.IsAny<CancellationToken>())).ReturnsAsync(new IngestionJobMetadataResponse { JobId = jobId });
        service.Setup(x => x.ClearFailedJobsAsync("unknown", It.IsAny<CancellationToken>())).ReturnsAsync(1);
        service.Setup(x => x.ClearFinishedJobsAsync("unknown", It.IsAny<CancellationToken>())).ReturnsAsync(1);
        service.Setup(x => x.RetryJobAsync(jobId, "unknown", It.IsAny<CancellationToken>())).ReturnsAsync(status);
        var sut = CreateController(Mock.Of<IBlobStorageService>(), service.Object);
        sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        (await sut.StartJobAsync(start, CancellationToken.None)).Should().BeOfType<AcceptedResult>();
        (await sut.ImportGraphAsync(graph, CancellationToken.None)).Should().BeOfType<AcceptedResult>();
        (await sut.GetJobStatusAsync(jobId, CancellationToken.None)).Should().BeOfType<OkObjectResult>();
        (await sut.SubmitManualMetadataAsync(jobId, metadata, CancellationToken.None)).Should().BeOfType<OkObjectResult>();
        (await sut.GetJobMetadataAsync(jobId, CancellationToken.None)).Should().BeOfType<OkObjectResult>();
        (await sut.DeleteJobAsync(jobId, CancellationToken.None)).Should().BeOfType<AcceptedResult>();
        (await sut.CancelJobAsync(jobId, CancellationToken.None)).Should().BeOfType<NoContentResult>();
        (await sut.FailJobAsync(jobId, "operator request", CancellationToken.None)).Should().BeOfType<NoContentResult>();
        (await sut.ClearFailedJobsAsync(CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
        (await sut.ClearFinishedJobsAsync(CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
        (await sut.RetryJobAsync(jobId, CancellationToken.None)).Should().BeOfType<AcceptedResult>();
    }

    [Theory]
    [InlineData("already being deleted", "Job deletion already in progress")]
    [InlineData("service rejected deletion", "Job deletion rejected")]
    public async Task DeleteJobAsync_WhenLegacyExceptionIsNotNotFound_MapsConflict(string message, string title)
    {
        var jobId = Guid.NewGuid();
        var service = new Mock<IIngestionJobService>();
        service.Setup(x => x.DeleteJobAsync(jobId, "test-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(message));

        var result = await CreateController(Mock.Of<IBlobStorageService>(), service.Object)
            .DeleteJobAsync(jobId, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>().Which.Value.Should().BeOfType<ProblemDetails>().Which.Title.Should().Be(title);
    }

    private static IngestionJobStartRequest CreateValidStartRequest() => new()
    {
        UploadId = Guid.NewGuid().ToString(),
        DocumentType = "manual-pdf",
        ProcessorRunId = Guid.NewGuid().ToString("N"),
        Configuration = new IngestionJobConfiguration { ExtractGraphRelationships = false, OcrEnabled = false }
    };

    private static IngestionJobsController CreateController(
        IBlobStorageService blobStorageService,
        IIngestionJobService? ingestionJobService = null,
        IngestionOptions? ingestionOptions = null,
        IChunkReprocessService? reprocessService = null)
    {
        var controller = new IngestionJobsController(
            ingestionJobService ?? Mock.Of<IIngestionJobService>(),
            new IngestionJobValidator(),
            blobStorageService,
            Options.Create(new BlobStorageOptions
            {
                AccountEndpoint = "https://storage.example",
                RawUploadsContainer = "raw-uploads"
            }),
            Options.Create(ingestionOptions ?? new IngestionOptions
            {
                MaxInputBytes = 2_000_000_000L
            }),
            reprocessService ?? Mock.Of<IChunkReprocessService>(),
            NullLogger<IngestionJobsController>.Instance);

        controller.ControllerContext = new ControllerContext {
            HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test-user")]))
            }
        };

        return controller;
    }

    private static IFormFile CreateFormFile(Stream stream, string fileName, string contentType)
    {
        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
