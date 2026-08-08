using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Presentation.API.Controllers;

public sealed class ProcessorArtifactsControllerTests
{
    [Fact]
    public void Constructor_NullDependencies_Throw()
    {
        var service = new Mock<IProcessorArtifactService>();
        Assert.Throws<ArgumentNullException>(() => new ProcessorArtifactsController(null!, NullLogger<ProcessorArtifactsController>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ProcessorArtifactsController(service.Object, null!));
    }

    [Fact]
    public async Task UploadArtifactAsync_MapsHttpFileToApplicationRequest()
    {
        var service = new Mock<IProcessorArtifactService>();
        ProcessorArtifactUploadRequest? submitted = null;
        service.Setup(x => x.UploadArtifactAsync(It.IsAny<ProcessorArtifactUploadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessorArtifactUploadRequest, CancellationToken>((request, _) => submitted = request)
            .ReturnsAsync(new ProcessorArtifactUploadResult(new ProcessorArtifactUploadResponse { UploadId = "00000000-0000-0000-0000-000000000001", ArtifactType = "search-chunks", BlobPath = "path" }, ProcessorArtifactOperationStatus.Success));
        var controller = Create(service.Object);

        var result = await controller.UploadArtifactAsync(CreateFile("{\"id\":\"chunk-1\"}"), "00000000-0000-0000-0000-000000000001", "search-chunks", CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
        submitted.Should().NotBeNull();
        submitted!.UploadId.Should().Be("00000000-0000-0000-0000-000000000001");
        submitted.ArtifactType.Should().Be("search-chunks");
        submitted.ContentType.Should().Be("application/x-ndjson");
    }

    // T1 (plan: 2026-08-03-processor-artifact-skip-observability, §4 T4 test-contract Row 4): RED test for the
    // not-yet-implemented ProcessorArtifactOperationStatus.IndexingSkipped => Accepted(...) controller arm.
    // Expected to fail to compile until T3 adds the enum member and T4 adds the controller switch arm; do not
    // implement production code changes here.
    [Fact]
    public async Task UploadArtifactAsync_MapsIndexingSkippedTo202AcceptedCarryingResponseBody_NotServerError()
    {
        var response = new ProcessorArtifactUploadResponse
        {
            UploadId = "00000000-0000-0000-0000-000000000001",
            ArtifactType = "search-chunks",
            BlobPath = "search-chunks/00000000-0000-0000-0000-000000000001/chunks.jsonl",
            Status = "stored-not-indexed"
        };
        var service = new Mock<IProcessorArtifactService>();
        service.Setup(x => x.UploadArtifactAsync(It.IsAny<ProcessorArtifactUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessorArtifactUploadResult(response, ProcessorArtifactOperationStatus.IndexingSkipped));

        var result = await Create(service.Object).UploadArtifactAsync(
            CreateFile("{\"id\":\"chunk-1\"}"), "00000000-0000-0000-0000-000000000001", "search-chunks", CancellationToken.None);

        // BeOfType (exact type), not BeAssignableTo: the discard "_ =>" arm returns a plain ObjectResult via
        // StatusCode(500, ...), so an exact AcceptedResult match rules out IndexingSkipped falling through to it.
        var accepted = result.Should().BeOfType<AcceptedResult>().Which;
        accepted.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        accepted.Value.Should().BeSameAs(response);
    }

    [Theory]
    [InlineData(ProcessorArtifactOperationStatus.InvalidUploadId)]
    [InlineData(ProcessorArtifactOperationStatus.InvalidArtifactType)]
    public async Task UploadArtifactAsync_MapsValidationResultsToBadRequest(ProcessorArtifactOperationStatus status)
    {
        var service = new Mock<IProcessorArtifactService>();
        service.Setup(x => x.UploadArtifactAsync(It.IsAny<ProcessorArtifactUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessorArtifactUploadResult(null, status));

        var result = await Create(service.Object).UploadArtifactAsync(CreateFile("content"), "00000000-0000-0000-0000-000000000001", "search-chunks", CancellationToken.None);

        Status(result, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task DownloadSourceAsync_MapsApplicationResults()
    {
        var service = new Mock<IProcessorArtifactService>();
        service.Setup(x => x.DownloadSourceAsync("valid", "manual-pdf", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessorArtifactSourceResult(new MemoryStream(), "application/pdf", ProcessorArtifactOperationStatus.Success));
        service.Setup(x => x.DownloadSourceAsync("missing", "manual-pdf", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessorArtifactSourceResult(null, null, ProcessorArtifactOperationStatus.NotFound));
        var controller = Create(service.Object);

        (await controller.DownloadSourceAsync("valid", "manual-pdf", CancellationToken.None)).Should().BeOfType<FileStreamResult>();
        Status(await controller.DownloadSourceAsync("missing", "manual-pdf", CancellationToken.None), StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task DownloadSourceWithAccessTokenAsync_DelegatesAccessTokenToUseCase()
    {
        var service = new Mock<IProcessorArtifactService>();
        service.Setup(x => x.DownloadSourceAsync("upload", "manual-pdf", "token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessorArtifactSourceResult(null, null, ProcessorArtifactOperationStatus.Unauthorized));

        var result = await Create(service.Object).DownloadSourceWithAccessTokenAsync("upload", "manual-pdf", "token", CancellationToken.None);

        Status(result, StatusCodes.Status401Unauthorized);
        service.Verify(x => x.DownloadSourceAsync("upload", "manual-pdf", "token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_MapsUseCaseResponseAndNotFound()
    {
        var service = new Mock<IProcessorArtifactService>();
        service.Setup(x => x.ReportJobStageByRunIdAsync("run", It.IsAny<IngestionJobStageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJobStatusResponse());
        service.Setup(x => x.ReportJobStageByRunIdAsync("missing", It.IsAny<IngestionJobStageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJobStatusResponse?)null);
        var controller = Create(service.Object);
        var request = new IngestionJobStageRequest { Stage = "extracting" };

        (await controller.ReportJobStageByRunIdAsync("run", request, CancellationToken.None)).Should().BeOfType<OkObjectResult>();
        Status(await controller.ReportJobStageByRunIdAsync("missing", request, CancellationToken.None), StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task UploadArtifactAsync_RejectsMissingAndOversizedFiles()
    {
        var controller = Create(Mock.Of<IProcessorArtifactService>());
        var oversized = new Mock<IFormFile>();
        oversized.SetupGet(x => x.Length).Returns(500L * 1024 * 1024 + 1);

        Status(await controller.UploadArtifactAsync(null, "upload", "search-chunks", CancellationToken.None), StatusCodes.Status400BadRequest);
        Status(await controller.UploadArtifactAsync(oversized.Object, "upload", "search-chunks", CancellationToken.None), StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UploadArtifactAsync_MapsUnexpectedResultAndExceptionToServerError()
    {
        var unexpected = new Mock<IProcessorArtifactService>();
        unexpected.Setup(x => x.UploadArtifactAsync(It.IsAny<ProcessorArtifactUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessorArtifactUploadResult(null, (ProcessorArtifactOperationStatus)999));
        Status(await Create(unexpected.Object).UploadArtifactAsync(CreateFile("content"), "upload", "search-chunks", CancellationToken.None), StatusCodes.Status500InternalServerError);

        var throwing = new Mock<IProcessorArtifactService>();
        throwing.Setup(x => x.UploadArtifactAsync(It.IsAny<ProcessorArtifactUploadRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage failure"));
        Status(await Create(throwing.Object).UploadArtifactAsync(CreateFile("content"), "upload", "search-chunks", CancellationToken.None), StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task UploadArtifactAsync_WhenContentTypeIsMissing_UsesOctetStream()
    {
        var service = new Mock<IProcessorArtifactService>();
        service.Setup(x => x.UploadArtifactAsync(It.Is<ProcessorArtifactUploadRequest>(request => request.ContentType == "application/octet-stream"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessorArtifactUploadResult(new ProcessorArtifactUploadResponse(), ProcessorArtifactOperationStatus.Success));
        var file = new Mock<IFormFile>();
        file.SetupGet(x => x.Length).Returns(7);
        file.SetupGet(x => x.ContentType).Returns((string?)null!);
        file.Setup(x => x.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((Stream target, CancellationToken ct) => target.WriteAsync("content"u8.ToArray(), ct).AsTask());

        var result = await Create(service.Object).UploadArtifactAsync(file.Object, "upload", "search-chunks", CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
        service.VerifyAll();
    }

    [Theory]
    [InlineData(ProcessorArtifactOperationStatus.InvalidUploadId, StatusCodes.Status400BadRequest)]
    [InlineData(ProcessorArtifactOperationStatus.InvalidDocumentType, StatusCodes.Status400BadRequest)]
    [InlineData((ProcessorArtifactOperationStatus)999, StatusCodes.Status500InternalServerError)]
    public async Task DownloadSourceAsync_MapsEveryRemainingStatus(ProcessorArtifactOperationStatus operationStatus, int expectedStatus)
    {
        var service = new Mock<IProcessorArtifactService>();
        service.Setup(x => x.DownloadSourceAsync("upload", "manual-pdf", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessorArtifactSourceResult(null, null, operationStatus));

        Status(await Create(service.Object).DownloadSourceAsync("upload", "manual-pdf", CancellationToken.None), expectedStatus);
    }

    [Fact]
    public async Task DownloadSourceAsync_WhenServiceThrows_ReturnsServerError()
    {
        var service = new Mock<IProcessorArtifactService>();
        service.Setup(x => x.DownloadSourceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("download failure"));

        Status(await Create(service.Object).DownloadSourceAsync("upload", "manual-pdf", CancellationToken.None), StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_RejectsMissingRequestStageAndRunId()
    {
        var controller = Create(Mock.Of<IProcessorArtifactService>());

        Status(await controller.ReportJobStageByRunIdAsync("run", null, CancellationToken.None), StatusCodes.Status400BadRequest);
        Status(await controller.ReportJobStageByRunIdAsync("run", new IngestionJobStageRequest { Stage = " " }, CancellationToken.None), StatusCodes.Status400BadRequest);
        Status(await controller.ReportJobStageByRunIdAsync(" ", new IngestionJobStageRequest { Stage = "extract" }, CancellationToken.None), StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ReportJobStageAsync_MapsValidationAndNotFoundExceptions()
    {
        var jobId = Guid.NewGuid();
        var service = new Mock<IProcessorArtifactService>();
        var controller = Create(service.Object);

        Status(await controller.ReportJobStageAsync(jobId, null, CancellationToken.None), StatusCodes.Status400BadRequest);
        service.Setup(x => x.ReportJobStageAsync(jobId, It.IsAny<IngestionJobStageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("missing"));
        Status(await controller.ReportJobStageAsync(jobId, new IngestionJobStageRequest { Stage = "extract" }, CancellationToken.None), StatusCodes.Status404NotFound);

        service.Setup(x => x.ReportJobStageAsync(jobId, It.IsAny<IngestionJobStageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("job not found"));
        Status(await controller.ReportJobStageAsync(jobId, new IngestionJobStageRequest { Stage = "extract" }, CancellationToken.None), StatusCodes.Status404NotFound);
    }

    private static ProcessorArtifactsController Create(IProcessorArtifactService service) =>
        new(service, NullLogger<ProcessorArtifactsController>.Instance);

    private static IFormFile CreateFile(string content)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return new FormFile(stream, 0, stream.Length, "file", "chunks.jsonl")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/x-ndjson"
        };
    }

    private static void Status(IActionResult result, int expected) =>
        result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(expected);
}
