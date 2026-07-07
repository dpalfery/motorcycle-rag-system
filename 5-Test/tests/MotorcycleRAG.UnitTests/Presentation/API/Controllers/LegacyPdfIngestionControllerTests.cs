using System.Collections.ObjectModel;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Presentation.API.Controllers;

public sealed class LegacyPdfIngestionControllerTests
{
    [Fact]
    public async Task DataPipelineProcessingController_ProcessAsync_WithPdfRequest_ReturnsBadRequest()
    {
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        var sut = new DataPipelineProcessingController(orchestrator.Object, NullLogger<DataPipelineProcessingController>.Instance) {
            ControllerContext = CreateControllerContext()
        };

        var request = CreatePdfRequest();

        var result = await sut.ProcessAsync(request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Legacy PDF ingestion is no longer supported");
        problem.Detail.Should().Contain("/api/ingestion/jobs/upload");
        problem.Detail.Should().Contain("/api/ingestion/jobs");
        orchestrator.Verify(service => service.ProcessFileAsync(It.IsAny<DataPipelineRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PipelineProcessingController_ProcessFileAsync_WithPdfRequest_ReturnsBadRequest()
    {
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        var sut = new PipelineProcessingController(orchestrator.Object, NullLogger<PipelineProcessingController>.Instance) {
            ControllerContext = CreateControllerContext()
        };

        var request = CreatePdfRequest();

        var result = await sut.ProcessFileAsync(request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Legacy PDF ingestion is no longer supported");
        orchestrator.Verify(service => service.ProcessFileAsync(It.IsAny<DataPipelineRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PipelineProcessingController_ProcessBatchAsync_WithPdfRequest_ReturnsBadRequest()
    {
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        var sut = new PipelineProcessingController(orchestrator.Object, NullLogger<PipelineProcessingController>.Instance) {
            ControllerContext = CreateControllerContext()
        };

        var requests = new Collection<DataPipelineRequest> {
            CreatePdfRequest(),
            new() {
                FileName = "inventory.csv",
                FilePath = @"D:\uploads\inventory.csv",
                FileType = FileType.CSV
            }
        };

        var result = await sut.ProcessBatchAsync(requests);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeOfType<ProblemDetails>();
        orchestrator.Verify(service => service.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FileUploadController_UploadFileWithProcessingAsync_ReturnsGone()
    {
        var sut = new FileUploadController(
            Options.Create(new FileUploadConfiguration()),
            NullLogger<FileUploadController>.Instance) {
            ControllerContext = CreateControllerContext()
        };

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("pdf"));
        var file = new FormFile(stream, 0, stream.Length, "file", "manual.pdf") {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var result = sut.UploadFileWithProcessingAsync(file, true);

        var gone = result.Should().BeOfType<ObjectResult>().Subject;
        gone.StatusCode.Should().Be(StatusCodes.Status410Gone);
        gone.Value.Should().BeOfType<ProblemDetails>()
            .Which.Title.Should().Be("Legacy disk upload is no longer supported");
    }

    [Fact]
    public async Task FileUploadController_UploadFilesWithProcessingAsync_ReturnsGone()
    {
        var sut = new FileUploadController(
            Options.Create(new FileUploadConfiguration()),
            NullLogger<FileUploadController>.Instance) {
            ControllerContext = CreateControllerContext()
        };

        await using var pdfStream = new MemoryStream(Encoding.UTF8.GetBytes("pdf"));
        var pdfFile = new FormFile(pdfStream, 0, pdfStream.Length, "file", "manual.pdf") {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        await using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes("a,b"));
        var csvFile = new FormFile(csvStream, 0, csvStream.Length, "file", "inventory.csv") {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };

        var result = sut.UploadFilesWithProcessingAsync(new[] { pdfFile, csvFile }, true);

        var gone = result.Should().BeOfType<ObjectResult>().Subject;
        gone.StatusCode.Should().Be(StatusCodes.Status410Gone);
        gone.Value.Should().BeOfType<ProblemDetails>()
            .Which.Detail.Should().Contain("/api/ingestion/jobs/upload");
    }

    private static ControllerContext CreateControllerContext() =>
        new() {
            HttpContext = new DefaultHttpContext()
        };

    private static DataPipelineRequest CreatePdfRequest() =>
        new() {
            FileName = "manual.pdf",
            FilePath = @"D:\uploads\manual.pdf",
            FileType = FileType.PDF,
            CreatedBy = "test"
        };
}
