using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
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
using MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

namespace MotorcycleRAG.API.Tests.Controllers;

public class ManualIngestionControllerTests
{
    private readonly Mock<IManualIngestionService> _mockService;
    private readonly Mock<ILogger<ManualIngestionController>> _mockLogger;
    private readonly ManualIngestionController _controller;

    public ManualIngestionControllerTests()
    {
        _mockService = new Mock<IManualIngestionService>();
        _mockLogger = new Mock<ILogger<ManualIngestionController>>();

        _controller = new ManualIngestionController(_mockService.Object, _mockLogger.Object);
        
        // Setup User with claims
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "test-user-id")
        }));
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task RegisterDocument_ValidRequest_ReturnsOk()
    {
        // Arrange
        var request = new RegisterManualDocumentRequest("test.pdf", "pdf", null, null, null, null);
        var mockFile = new Mock<IFormFile>();
        var content = "test content";
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
        await writer.FlushAsync();
        stream.Position = 0;
        
        mockFile.Setup(f => f.Length).Returns(stream.Length);
        mockFile.Setup(f => f.OpenReadStream()).Returns(stream);

        var dto = new ManualDocumentDto(Guid.NewGuid(), "test.pdf", "pdf", null, null, "test-user", null, null, null, DateTimeOffset.UtcNow, ManualDocumentStatus.Pending, null, null, null);
        _mockService.Setup(s => s.RegisterManualAsync(request, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        // Act
        var result = await _controller.RegisterDocument(request, mockFile.Object, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedDto = okResult.Value.Should().BeAssignableTo<ManualDocumentDto>().Subject;
        returnedDto.Should().Be(dto);
    }
    
    [Fact]
    public async Task RegisterDocument_NullFile_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.RegisterDocument(new RegisterManualDocumentRequest("test.pdf", "pdf", null, null, null, null), null!, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetDocument_ValidId_ReturnsOk()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new ManualDocumentDto(id, "test.pdf", "pdf", null, null, "test-user", null, null, null, DateTimeOffset.UtcNow, ManualDocumentStatus.Pending, null, null, null);
        _mockService.Setup(s => s.GetDocumentAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        // Act
        var result = await _controller.GetDocument(id, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedDto = okResult.Value.Should().BeAssignableTo<ManualDocumentDto>().Subject;
        returnedDto.Should().Be(dto);
    }
    
    [Fact]
    public async Task GetDocument_NotFound_ReturnsNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockService.Setup(s => s.GetDocumentAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((ManualDocumentDto)null!);

        // Act
        var result = await _controller.GetDocument(id, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetAllDocuments_ReturnsOk()
    {
        // Arrange
        var dtos = new[] { new ManualDocumentDto(Guid.NewGuid(), "a", "b", null, null, "c", null, null, null, DateTimeOffset.UtcNow, ManualDocumentStatus.Pending, null, null, null) };
        _mockService.Setup(s => s.GetAllDocumentsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(dtos);

        // Act
        var result = await _controller.GetAllDocuments(CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedDtos = okResult.Value.Should().BeAssignableTo<IEnumerable<ManualDocumentDto>>().Subject;
        returnedDtos.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateRun_ValidRequest_ReturnsOk()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = new CreateManualRunRequest(id, "test", null, null);
        var dto = new ManualRunDto(Guid.NewGuid(), id, "test", DateTimeOffset.UtcNow, null, (ManualRunStatus)0, null, null, null, null, null, null, null, null, null);
        _mockService.Setup(s => s.CreateRunAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        // Act
        var result = await _controller.CreateRun(id, request, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(dto);
    }
    
    [Fact]
    public async Task CreateRun_IdMismatch_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.CreateRun(Guid.NewGuid(), new CreateManualRunRequest(Guid.NewGuid(), "test", null, null), CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetRunsForDocument_ReturnsOk()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dtos = new[] { new ManualRunDto(Guid.NewGuid(), id, "test", DateTimeOffset.UtcNow, null, (ManualRunStatus)0, null, null, null, null, null, null, null, null, null) };
        _mockService.Setup(s => s.GetRunsForDocumentAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(dtos);

        // Act
        var result = await _controller.GetRunsForDocument(id, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeEquivalentTo(dtos);
    }

    [Fact]
    public async Task GetRun_ValidId_ReturnsOk()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new ManualRunDto(id, Guid.NewGuid(), "test", DateTimeOffset.UtcNow, null, (ManualRunStatus)0, null, null, null, null, null, null, null, null, null);
        _mockService.Setup(s => s.GetRunAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        // Act
        var result = await _controller.GetRun(id, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(dto);
    }
    
    [Fact]
    public async Task GetRun_NotFound_ReturnsNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockService.Setup(s => s.GetRunAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((ManualRunDto)null!);

        // Act
        var result = await _controller.GetRun(id, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetStagesForRun_ReturnsOk()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dtos = new[] { new ManualStageDto(Guid.NewGuid(), id, "stage1", (ManualStageStatus)0, DateTimeOffset.UtcNow, null, null, null, null, null) };
        _mockService.Setup(s => s.GetStagesForRunAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(dtos);

        // Act
        var result = await _controller.GetStagesForRun(id, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeEquivalentTo(dtos);
    }

    [Fact]
    public async Task ReportStageStart_ReturnsOk()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = new ManualStageStartRequest(null);

        // Act
        var result = await _controller.ReportStageStart(id, "stage1", request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();
        _mockService.Verify(s => s.ReportStageStartAsync(id, "stage1", request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReportStageComplete_ReturnsOk()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = new ManualStageCompleteRequest(null, null, null);

        // Act
        var result = await _controller.ReportStageComplete(id, "stage1", request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();
        _mockService.Verify(s => s.ReportStageCompleteAsync(id, "stage1", request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReportStageFail_ReturnsOk()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = new ManualStageFailRequest("error", null);

        // Act
        var result = await _controller.ReportStageFail(id, "stage1", request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();
        _mockService.Verify(s => s.ReportStageFailAsync(id, "stage1", request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterArtifact_ReturnsOk()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = new ManualArtifactRegistrationRequest("path", "type", null, null, null);

        // Act
        var result = await _controller.RegisterArtifact(id, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();
        _mockService.Verify(s => s.RegisterArtifactAsync(id, request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateGraphSeedJob_ReturnsAccepted()
    {
        // Arrange
        var request = new CreateGraphSeedJobRequest("file", null);
        var response = new IngestionJobStatusResponse();
        _mockService.Setup(s => s.CreateGraphSeedJobAsync(request, "test-user-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.CreateGraphSeedJob(request, CancellationToken.None);

        // Assert
        var acceptedResult = result.Result.Should().BeOfType<AcceptedResult>().Subject;
        acceptedResult.Value.Should().Be(response);
    }

    [Fact]
    public async Task GetOperations_ReturnsOk()
    {
        // Arrange
        var dtos = new[] { new UnifiedOperationDto("op1", "type1", "status", DateTimeOffset.UtcNow, null, null, null, null) };
        _mockService.Setup(s => s.GetOperationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(dtos);

        // Act
        var result = await _controller.GetOperations(CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeEquivalentTo(dtos);
    }
}
