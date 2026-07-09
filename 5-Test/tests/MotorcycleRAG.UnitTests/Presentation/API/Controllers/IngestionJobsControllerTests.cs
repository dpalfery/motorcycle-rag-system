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

    private static IngestionJobsController CreateController(
        IBlobStorageService blobStorageService,
        IIngestionJobService? ingestionJobService = null,
        IngestionOptions? ingestionOptions = null)
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
            Mock.Of<IChunkReprocessService>(),
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
