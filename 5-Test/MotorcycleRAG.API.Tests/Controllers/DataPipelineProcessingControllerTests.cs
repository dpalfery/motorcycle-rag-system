using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Presentation.API.Controllers;

public sealed class DataPipelineProcessingControllerTests
{
    // === GET /api/DataPipeline/status/{executionId} ===

    [Fact]
    public async Task GetStatusAsync_WhenExecutionIdMissing_ReturnsBadRequest()
    {
        var sut = CreateController(Mock.Of<IDataPipelineOrchestrator>());

        var result = await sut.GetStatusAsync(string.Empty);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Execution ID is required");
    }

    [Fact]
    public async Task GetStatusAsync_WhenOrchestratorSucceeds_ReturnsOkWithExecutionIdAndStatus()
    {
        var executionId = "legacy-exec-1";
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator
            .Setup(o => o.GetPipelineStatusAsync(executionId))
            .ReturnsAsync(PipelineStatus.Completed);

        var sut = CreateController(orchestrator.Object);

        var result = await sut.GetStatusAsync(executionId);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new { executionId, status = PipelineStatus.Completed });
        orchestrator.Verify(o => o.GetPipelineStatusAsync(executionId), Times.Once);
    }

    [Fact]
    public async Task GetStatusAsync_WhenOrchestratorThrows_ReturnsInternalServerError()
    {
        var executionId = "legacy-exec-2";
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator
            .Setup(o => o.GetPipelineStatusAsync(executionId))
            .ThrowsAsync(new InvalidOperationException("execution not found"));

        var sut = CreateController(orchestrator.Object);

        var result = await sut.GetStatusAsync(executionId);

        var statusCode = result.Should().BeOfType<ObjectResult>().Subject;
        statusCode.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var problem = statusCode.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Internal server error");
        problem.Detail.Should().NotContain("execution not found");
    }

    [Fact]
    public void Constructor_RejectsBothDependencies()
    {
        var orchestrator = Mock.Of<IDataPipelineOrchestrator>();

        Assert.Throws<ArgumentNullException>(() => new DataPipelineProcessingController(null!, NullLogger<DataPipelineProcessingController>.Instance));
        Assert.Throws<ArgumentNullException>(() => new DataPipelineProcessingController(orchestrator, null!));
    }

    [Fact]
    public async Task ProcessAsync_MapsMissingRequestSuccessAndFailure()
    {
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        var sut = CreateController(orchestrator.Object);
        sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        (await sut.ProcessAsync(null!)).Should().BeOfType<BadRequestObjectResult>();

        var request = new DataPipelineRequest { FileName = "data.csv", FilePath = "opaque-upload-id", FileType = FileType.CSV };
        orchestrator.Setup(x => x.ProcessFileAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(new PipelineExecutionResult());
        (await sut.ProcessAsync(request)).Should().BeOfType<OkObjectResult>();

        orchestrator.Setup(x => x.ProcessFileAsync(request, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("unavailable"));
        (await sut.ProcessAsync(request)).Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    private static DataPipelineProcessingController CreateController(IDataPipelineOrchestrator orchestrator) =>
        new(orchestrator, NullLogger<DataPipelineProcessingController>.Instance);
}
