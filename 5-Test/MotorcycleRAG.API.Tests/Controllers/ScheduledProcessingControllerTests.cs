using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.API.Tests.Controllers;

public class ScheduledProcessingControllerTests
{
    private readonly Mock<IScheduledPipelineService> _mockService;
    private readonly Mock<ILogger<ScheduledProcessingController>> _mockLogger;
    private readonly ScheduledProcessingController _controller;

    public ScheduledProcessingControllerTests()
    {
        _mockService = new Mock<IScheduledPipelineService>();
        _mockLogger = new Mock<ILogger<ScheduledProcessingController>>();

        _controller = new ScheduledProcessingController(_mockService.Object, _mockLogger.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public async Task ExecuteScheduledProcessingAsync_ReturnsOk()
    {
        // Arrange
        var resultDto = new PipelineExecutionResult { ExecutionId = "test" };
        _mockService.Setup(s => s.ExecuteImmediateRunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultDto);

        // Act
        var result = await _controller.ExecuteScheduledProcessingAsync();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(resultDto);
    }
    
    [Fact]
    public async Task ExecuteScheduledProcessingAsync_Cancellation_Returns499()
    {
        // Arrange
        _mockService.Setup(s => s.ExecuteImmediateRunAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _controller.ExecuteScheduledProcessingAsync();

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(499);
        var problemDetails = statusResult.Value.Should().BeOfType<ProblemDetails>().Subject;
        problemDetails.Status.Should().Be(499);
    }
    
    [Fact]
    public async Task ExecuteScheduledProcessingAsync_Exception_Returns500()
    {
        // Arrange
        _mockService.Setup(s => s.ExecuteImmediateRunAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("test"));

        // Act
        var result = await _controller.ExecuteScheduledProcessingAsync();

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var problemDetails = statusResult.Value.Should().BeOfType<ProblemDetails>().Subject;
        problemDetails.Status.Should().Be(500);
    }

    [Fact]
    public async Task GetScheduledProcessingStatsAsync_ReturnsOk()
    {
        // Arrange
        var stats = new ScheduledProcessingStats { TotalScheduledRuns = 5 };
        _mockService.Setup(s => s.GetProcessingStatsAsync())
            .ReturnsAsync(stats);

        // Act
        var result = await _controller.GetScheduledProcessingStatsAsync();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(stats);
    }
    
    [Fact]
    public async Task GetScheduledProcessingStatsAsync_Exception_Returns500()
    {
        // Arrange
        _mockService.Setup(s => s.GetProcessingStatsAsync())
            .ThrowsAsync(new Exception("test"));

        // Act
        var result = await _controller.GetScheduledProcessingStatsAsync();

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var problemDetails = statusResult.Value.Should().BeOfType<ProblemDetails>().Subject;
        problemDetails.Status.Should().Be(500);
    }

    [Fact]
    public void Constructor_RejectsBothDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new ScheduledProcessingController(null!, _mockLogger.Object));
        Assert.Throws<ArgumentNullException>(() => new ScheduledProcessingController(_mockService.Object, null!));
    }
}
