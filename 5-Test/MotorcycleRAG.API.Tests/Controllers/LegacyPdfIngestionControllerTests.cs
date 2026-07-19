using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

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
