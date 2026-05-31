using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Application.Pipeline;
using MotorcycleRAG.Application.Pipeline.Validators;
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
    public async Task DeleteJobAsync_WhenJobIsRunning_ReturnsConflict() {
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

        var sut = CreateController(Mock.Of<IBlobStorageService>(), ingestionJobs.Object);

        var result = await sut.DeleteJobAsync(jobId, CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeOfType<ProblemDetails>()
            .Which.Title.Should().Be("Only terminal jobs can be deleted");
        ingestionJobs.Verify(
            service => service.DeleteJobAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

    private static IngestionJobsController CreateController(
        IBlobStorageService blobStorageService,
        IIngestionJobService? ingestionJobService = null)
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
