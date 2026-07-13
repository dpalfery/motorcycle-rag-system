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
        ((Action)(() => new ProcessorArtifactsController(null!, NullLogger<ProcessorArtifactsController>.Instance))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new ProcessorArtifactsController(service.Object, null!))).Should().Throw<ArgumentNullException>();
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
