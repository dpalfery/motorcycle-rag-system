using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.API.Tests.Controllers;

public class PipelineMonitoringControllerTests
{
    private readonly Mock<IPipelineMonitoringService> _mockService;
    private readonly Mock<ILogger<PipelineMonitoringController>> _mockLogger;
    private readonly PipelineMonitoringController _controller;

    public PipelineMonitoringControllerTests()
    {
        _mockService = new Mock<IPipelineMonitoringService>();
        _mockLogger = new Mock<ILogger<PipelineMonitoringController>>();

        _controller = new PipelineMonitoringController(_mockService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetPipelineHealthAsync_ReturnsOk()
    {
        // Arrange
        var health = new PipelineHealthStatus { Status = OverallHealthStatus.Healthy };
        _mockService.Setup(s => s.GetHealthStatusAsync())
            .ReturnsAsync(health);

        // Act
        var result = await _controller.GetPipelineHealthAsync();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(health);
    }
    
    [Fact]
    public async Task GetPipelineHealthAsync_Exception_Returns500()
    {
        // Arrange
        _mockService.Setup(s => s.GetHealthStatusAsync())
            .ThrowsAsync(new Exception("test"));

        // Act
        var result = await _controller.GetPipelineHealthAsync();

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var problemDetails = statusResult.Value.Should().BeOfType<ProblemDetails>().Subject;
        problemDetails.Status.Should().Be(500);
    }
}
