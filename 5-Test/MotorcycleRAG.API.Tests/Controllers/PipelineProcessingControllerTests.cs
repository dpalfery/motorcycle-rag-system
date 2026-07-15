using System;
using System.Collections.ObjectModel;
using System.IO;
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

public sealed class PipelineProcessingControllerTests
{
    // === POST /api/pipeline-processing/cancel/{executionId} ===

    [Fact]
    public async Task CancelPipelineAsync_WhenExecutionIdMissing_ReturnsBadRequest()
    {
        var sut = CreateController(Mock.Of<IDataPipelineOrchestrator>());

        var result = await sut.CancelPipelineAsync(string.Empty);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CancelPipelineAsync_WhenOrchestratorCancelsSuccessfully_ReturnsOkWithCancelledTrue()
    {
        var executionId = "exec-123";
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator
            .Setup(o => o.CancelPipelineAsync(executionId))
            .ReturnsAsync(true);

        var sut = CreateController(orchestrator.Object);

        var result = await sut.CancelPipelineAsync(executionId);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<CancelPipelineResponse>().Subject;
        response.ExecutionId.Should().Be(executionId);
        response.Cancelled.Should().BeTrue();
        orchestrator.Verify(o => o.CancelPipelineAsync(executionId), Times.Once);
    }

    [Fact]
    public async Task CancelPipelineAsync_WhenOrchestratorReturnsFalse_ReturnsOkWithCancelledFalse()
    {
        var executionId = "exec-456";
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator
            .Setup(o => o.CancelPipelineAsync(executionId))
            .ReturnsAsync(false);

        var sut = CreateController(orchestrator.Object);

        var result = await sut.CancelPipelineAsync(executionId);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<CancelPipelineResponse>().Subject;
        response.ExecutionId.Should().Be(executionId);
        response.Cancelled.Should().BeFalse();
    }

    [Fact]
    public async Task CancelPipelineAsync_WhenOrchestratorThrows_ReturnsInternalServerError()
    {
        var executionId = "exec-789";
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator
            .Setup(o => o.CancelPipelineAsync(executionId))
            .ThrowsAsync(new InvalidOperationException("orchestrator unavailable"));

        var sut = CreateController(orchestrator.Object);

        var result = await sut.CancelPipelineAsync(executionId);

        var statusCode = result.Should().BeOfType<ObjectResult>().Subject;
        statusCode.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var problem = statusCode.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Internal server error");
        problem.Detail.Should().NotContain("orchestrator unavailable");
    }

    // === GET /api/pipeline-processing/status/{executionId} ===

    [Fact]
    public async Task GetPipelineStatusAsync_WhenExecutionIdMissing_ReturnsBadRequest()
    {
        var sut = CreateController(Mock.Of<IDataPipelineOrchestrator>());

        var result = await sut.GetPipelineStatusAsync(string.Empty);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetPipelineStatusAsync_WhenOrchestratorSucceeds_ReturnsOkWithStatus()
    {
        var executionId = "exec-abc";
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator
            .Setup(o => o.GetPipelineStatusAsync(executionId))
            .ReturnsAsync(PipelineStatus.Processing);

        var sut = CreateController(orchestrator.Object);

        var result = await sut.GetPipelineStatusAsync(executionId);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<PipelineStatusResponse>().Subject;
        response.ExecutionId.Should().Be(executionId);
        response.Status.Should().Be(PipelineStatus.Processing);
        orchestrator.Verify(o => o.GetPipelineStatusAsync(executionId), Times.Once);
    }

    [Fact]
    public async Task GetPipelineStatusAsync_WhenOrchestratorThrows_ReturnsInternalServerError()
    {
        var executionId = "exec-def";
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator
            .Setup(o => o.GetPipelineStatusAsync(executionId))
            .ThrowsAsync(new InvalidOperationException("execution not found"));

        var sut = CreateController(orchestrator.Object);

        var result = await sut.GetPipelineStatusAsync(executionId);

        var statusCode = result.Should().BeOfType<ObjectResult>().Subject;
        statusCode.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var problem = statusCode.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Internal server error");
        problem.Detail.Should().NotContain("execution not found");
    }

    // === POST /api/pipeline-processing/process ===

    [Fact]
    public async Task ProcessFileAsync_WhenRequestIsNull_ReturnsBadRequest()
    {
        var sut = CreateControllerWithHttpContext(Mock.Of<IDataPipelineOrchestrator>());

        var result = await sut.ProcessFileAsync(null!);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("Request cannot be null");
    }

    [Fact]
    public async Task ProcessFileAsync_WhenFilePathOutsideUploads_ReturnsBadRequest()
    {
        var sut = CreateControllerWithHttpContext(Mock.Of<IDataPipelineOrchestrator>());
        var request = new DataPipelineRequest { FileName = "x.csv", FilePath = "/tmp/outside-uploads.csv", FileType = FileType.CSV };

        var result = await sut.ProcessFileAsync(request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("File does not exist at the specified path or path is not allowed");
    }

    [Fact]
    public async Task ProcessFileAsync_WhenFilePathIsBlank_ReturnsBadRequest()
    {
        var sut = CreateControllerWithHttpContext(Mock.Of<IDataPipelineOrchestrator>());

        var result = await sut.ProcessFileAsync(new DataPipelineRequest { FileName = "x.csv", FilePath = " ", FileType = FileType.CSV });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ProcessFileAsync_WhenSafePathDoesNotExist_ReturnsBadRequest()
    {
        var sut = CreateControllerWithHttpContext(Mock.Of<IDataPipelineOrchestrator>());
        var missing = Path.Combine(GetUploadsRoot(), "missing-" + Guid.NewGuid() + ".csv");
        var request = new DataPipelineRequest { FileName = "x.csv", FilePath = missing, FileType = FileType.CSV };

        var result = await sut.ProcessFileAsync(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ProcessFileAsync_WhenSafeFileExists_ReturnsOkWithResult()
    {
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator
            .Setup(o => o.ProcessFileAsync(It.IsAny<DataPipelineRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PipelineExecutionResult());
        var sut = CreateControllerWithHttpContext(orchestrator.Object);

        var filePath = Path.Combine(GetUploadsRoot(), "real-" + Guid.NewGuid() + ".csv");
        await File.WriteAllTextAsync(filePath, "make,model");
        try
        {
            var request = new DataPipelineRequest { FileName = "x.csv", FilePath = filePath, FileType = FileType.CSV };

            var result = await sut.ProcessFileAsync(request);

            result.Should().BeOfType<OkObjectResult>();
            orchestrator.Verify(o => o.ProcessFileAsync(It.IsAny<DataPipelineRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ProcessFileAsync_WhenCancelled_Returns499()
    {
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator
            .Setup(o => o.ProcessFileAsync(It.IsAny<DataPipelineRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var sut = CreateControllerWithHttpContext(orchestrator.Object);

        var filePath = Path.Combine(GetUploadsRoot(), "real-" + Guid.NewGuid() + ".csv");
        await File.WriteAllTextAsync(filePath, "make,model");
        try
        {
            var result = await sut.ProcessFileAsync(new DataPipelineRequest { FileName = "x.csv", FilePath = filePath, FileType = FileType.CSV });

            var statusCode = result.Should().BeOfType<ObjectResult>().Subject;
            statusCode.StatusCode.Should().Be(499);
            statusCode.Value.Should().BeOfType<ProblemDetails>().Which.Title.Should().Be("Request cancelled");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ProcessFileAsync_WhenOrchestratorThrows_ReturnsInternalServerError()
    {
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator
            .Setup(o => o.ProcessFileAsync(It.IsAny<DataPipelineRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateControllerWithHttpContext(orchestrator.Object);

        var filePath = Path.Combine(GetUploadsRoot(), "real-" + Guid.NewGuid() + ".csv");
        await File.WriteAllTextAsync(filePath, "make,model");
        try
        {
            var result = await sut.ProcessFileAsync(new DataPipelineRequest { FileName = "x.csv", FilePath = filePath, FileType = FileType.CSV });

            var statusCode = result.Should().BeOfType<ObjectResult>().Subject;
            statusCode.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
            statusCode.Value.Should().BeOfType<ProblemDetails>()
                .Which.Detail.Should().NotContain("boom");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    // === POST /api/pipeline-processing/process-batch ===

    [Fact]
    public async Task ProcessBatchAsync_WhenNull_ReturnsBadRequest()
    {
        var sut = CreateControllerWithHttpContext(Mock.Of<IDataPipelineOrchestrator>());

        var result = await sut.ProcessBatchAsync(null!);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("No processing requests provided");
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenEmpty_ReturnsBadRequest()
    {
        var sut = CreateControllerWithHttpContext(Mock.Of<IDataPipelineOrchestrator>());

        var result = await sut.ProcessBatchAsync(new Collection<DataPipelineRequest>());

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenValid_ReturnsOkWithResult()
    {
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator
            .Setup(o => o.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchPipelineResult());
        var sut = CreateControllerWithHttpContext(orchestrator.Object);

        var requests = new Collection<DataPipelineRequest>
        {
            new() { FileName = "a.csv", FileType = FileType.CSV }
        };

        var result = await sut.ProcessBatchAsync(requests);

        result.Should().BeOfType<OkObjectResult>();
        orchestrator.Verify(o => o.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenCancelled_Returns499()
    {
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator
            .Setup(o => o.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var sut = CreateControllerWithHttpContext(orchestrator.Object);

        var result = await sut.ProcessBatchAsync(new Collection<DataPipelineRequest> { new() { FileName = "a.csv", FileType = FileType.CSV } });

        var statusCode = result.Should().BeOfType<ObjectResult>().Subject;
        statusCode.StatusCode.Should().Be(499);
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenOrchestratorThrows_ReturnsInternalServerError()
    {
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator
            .Setup(o => o.ProcessBatchAsync(It.IsAny<IEnumerable<DataPipelineRequest>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateControllerWithHttpContext(orchestrator.Object);

        var result = await sut.ProcessBatchAsync(new Collection<DataPipelineRequest> { new() { FileName = "a.csv", FileType = FileType.CSV } });

        var statusCode = result.Should().BeOfType<ObjectResult>().Subject;
        statusCode.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    // === GET /api/pipeline-processing/metrics ===

    [Fact]
    public async Task GetPipelineMetricsAsync_DefaultWindow_ReturnsOk()
    {
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator.Setup(o => o.GetPipelineMetricsAsync(It.IsAny<TimeSpan?>())).ReturnsAsync(new PipelineMetrics());
        var sut = CreateController(orchestrator.Object);

        var result = await sut.GetPipelineMetricsAsync();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<PipelineMetrics>();
    }

    [Fact]
    public async Task GetPipelineMetricsAsync_DefaultWindow_WhenThrows_Returns500()
    {
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator.Setup(o => o.GetPipelineMetricsAsync(It.IsAny<TimeSpan?>())).ThrowsAsync(new InvalidOperationException());
        var sut = CreateController(orchestrator.Object);

        var result = await sut.GetPipelineMetricsAsync();

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public async Task GetPipelineMetricsAsync_WithHours_ClampsWindowAndReturnsOk(int hours)
    {
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator.Setup(o => o.GetPipelineMetricsAsync(It.Is<TimeSpan?>(ts => ts == TimeSpan.FromHours(Math.Max(1, Math.Min(168, hours))))))
            .ReturnsAsync(new PipelineMetrics());
        var sut = CreateController(orchestrator.Object);

        var result = await sut.GetPipelineMetricsAsync(hours);

        result.Should().BeOfType<OkObjectResult>();
        orchestrator.VerifyAll();
    }

    [Fact]
    public async Task GetPipelineMetricsAsync_WithHours_WhenThrows_Returns500()
    {
        var orchestrator = new Mock<IDataPipelineOrchestrator>();
        orchestrator.Setup(o => o.GetPipelineMetricsAsync(It.IsAny<TimeSpan?>())).ThrowsAsync(new InvalidOperationException());
        var sut = CreateController(orchestrator.Object);

        var result = await sut.GetPipelineMetricsAsync(12);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void IsSafeFilePath_WhenWhitespace_ReturnsFalse()
    {
        // The whitespace branch is unreachable via ProcessFileAsync (it null-checks the path first),
        // so exercise the private helper directly.
        var sut = CreateController(Mock.Of<IDataPipelineOrchestrator>());
        var method = typeof(PipelineProcessingController).GetMethod(
            "IsSafeFilePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(sut, new object?[] { "   " })!;

        result.Should().BeFalse();
    }

    [Fact]
    public void Constructor_RejectsBothDependencies()
    {
        var orchestrator = Mock.Of<IDataPipelineOrchestrator>();

        ((Action)(() => new PipelineProcessingController(null!, NullLogger<PipelineProcessingController>.Instance))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new PipelineProcessingController(orchestrator, null!))).Should().Throw<ArgumentNullException>();
    }

    private static string GetUploadsRoot()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "uploads"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static PipelineProcessingController CreateControllerWithHttpContext(IDataPipelineOrchestrator orchestrator)
    {
        var controller = new PipelineProcessingController(orchestrator, NullLogger<PipelineProcessingController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static PipelineProcessingController CreateController(IDataPipelineOrchestrator orchestrator) =>
        new(orchestrator, NullLogger<PipelineProcessingController>.Instance);
}
