using System;
using System.Collections.Generic;
using System.IO;
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

public class ManualsControllerTests
{
    private readonly Mock<IManualPageQueryService> _mockService;
    private readonly Mock<ILogger<ManualsController>> _mockLogger;
    private readonly ManualsController _controller;

    public ManualsControllerTests()
    {
        _mockService = new Mock<IManualPageQueryService>();
        _mockLogger = new Mock<ILogger<ManualsController>>();

        _controller = new ManualsController(_mockService.Object, _mockLogger.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public void Constructor_WhenDependencyIsNull_ThrowsArgumentNullException()
    {
        var nullQueryService = () => new ManualsController(null!, _mockLogger.Object);
        var nullLogger = () => new ManualsController(_mockService.Object, null!);

        nullQueryService.Should().Throw<ArgumentNullException>().WithParameterName("queryService");
        nullLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task GetPage_ValidRequest_ReturnsFile()
    {
        // Arrange
        var manualId = Guid.NewGuid();
        var pageNumber = 1;
        var stream = new MemoryStream();
        var resultDto = new ManualPageResult(stream, "etag1");
        
        _mockService.Setup(s => s.GetPageAsync(manualId, pageNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultDto);

        // Act
        var result = await _controller.GetPage(manualId, pageNumber, CancellationToken.None);

        // Assert
        var fileResult = result.Should().BeOfType<FileStreamResult>().Subject;
        fileResult.ContentType.Should().Be("image/png");
        fileResult.FileStream.Should().BeSameAs(stream);
        
        _controller.Response.Headers.CacheControl.ToString().Should().Contain("private, max-age=600");
        _controller.Response.Headers.ContentDisposition.ToString().Should().Contain($"inline; filename=\"{manualId}-p{pageNumber}.png\"");
        _controller.Response.Headers.ETag.ToString().Should().Be("etag1");
    }

    [Fact]
    public async Task GetPage_InvalidPageNumber_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetPage(Guid.NewGuid(), 0, CancellationToken.None);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problemDetails = badRequestResult.Value.Should().BeOfType<ProblemDetails>().Subject;
        problemDetails.Status.Should().Be(400);
    }
    
    [Fact]
    public async Task GetPage_NotFound_ReturnsNotFound()
    {
        // Arrange
        var manualId = Guid.NewGuid();
        _mockService.Setup(s => s.GetPageAsync(manualId, 1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        // Act
        var result = await _controller.GetPage(manualId, 1, CancellationToken.None);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var problemDetails = notFoundResult.Value.Should().BeOfType<ProblemDetails>().Subject;
        problemDetails.Status.Should().Be(404);
    }
}
