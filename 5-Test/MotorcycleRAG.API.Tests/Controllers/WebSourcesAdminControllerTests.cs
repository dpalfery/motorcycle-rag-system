using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.API.Tests.Controllers;

public class WebSourcesAdminControllerTests
{
    private readonly Mock<IWebSourceRepository> _mockRepo;
    private readonly Mock<ICurrentUserService> _mockUserService;
    private readonly Mock<ILogger<WebSourceRegistryService>> _mockServiceLogger;
    private readonly Mock<ILogger<WebSourcesAdminController>> _mockControllerLogger;
    private readonly WebSourceRegistryService _webSourceRegistryService;
    private readonly WebSourcesAdminController _controller;

    public WebSourcesAdminControllerTests()
    {
        _mockRepo = new Mock<IWebSourceRepository>();
        _mockUserService = new Mock<ICurrentUserService>();
        _mockServiceLogger = new Mock<ILogger<WebSourceRegistryService>>();
        _mockControllerLogger = new Mock<ILogger<WebSourcesAdminController>>();

        _webSourceRegistryService = new WebSourceRegistryService(
            _mockRepo.Object,
            _mockUserService.Object,
            _mockServiceLogger.Object);

        _controller = new WebSourcesAdminController(
            _webSourceRegistryService,
            _mockControllerLogger.Object);
            
        // Setup user as admin for all tests
        _mockUserService.Setup(u => u.IsAuthenticated).Returns(true);
        _mockUserService.Setup(u => u.IsInRole("mcr-api-admin")).Returns(true);
        _mockUserService.Setup(u => u.UserId).Returns("admin-user-id");
    }

    [Fact]
    public async Task GetAllWebSourcesAsync_ReturnsOkWithSources()
    {
        // Arrange
        var sources = new[] { new WebSource { Id = 1, Name = "Source 1" } };
        _mockRepo.Setup(r => r.GetAllWebSourcesAsync()).ReturnsAsync(sources);

        // Act
        var result = await _controller.GetAllWebSourcesAsync();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedSources = okResult.Value.Should().BeAssignableTo<WebSource[]>().Subject;
        returnedSources.Should().HaveCount(1);
        returnedSources[0].Name.Should().Be("Source 1");
    }

    [Fact]
    public async Task GetWebSourceByIdAsync_InvalidId_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetWebSourceByIdAsync(0);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetWebSourceByIdAsync_ValidId_ReturnsSource()
    {
        // Arrange
        var source = new WebSource { Id = 1, Name = "Source 1" };
        _mockRepo.Setup(r => r.GetWebSourceByIdAsync(1)).ReturnsAsync(source);

        // Act
        var result = await _controller.GetWebSourceByIdAsync(1);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedSource = okResult.Value.Should().BeAssignableTo<WebSource>().Subject;
        returnedSource.Id.Should().Be(1);
    }

    [Fact]
    public async Task GetWebSourceByIdAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetWebSourceByIdAsync(1)).ReturnsAsync((WebSource)null!);

        // Act
        var result = await _controller.GetWebSourceByIdAsync(1);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CreateWebSourceAsync_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var request = new CreateWebSourceRequest
        {
            Url = new Uri("https://example.com"),
            Name = "Example",
            Description = "Example source",
            IsEnabled = true,
            TrustTier = 3,
            CrawlFrequencyHours = 24,
            IncludeInSearch = true,
            MaxCrawlDepth = 2
        };

        _mockRepo.Setup(r => r.GetWebSourceByUrlAsync(It.IsAny<Uri>())).ReturnsAsync((WebSource)null!);
        _mockRepo.Setup(r => r.CreateWebSourceAsync(It.IsAny<WebSource>())).ReturnsAsync(new WebSource { Id = 1, Url = "https://example.com", Name = "Example" });

        // Act
        var result = await _controller.CreateWebSourceAsync(request);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedResult>().Subject;
        var returnedSource = createdResult.Value.Should().BeAssignableTo<WebSource>().Subject;
        returnedSource.Id.Should().Be(1);
    }
    
    [Fact]
    public async Task CreateWebSourceAsync_NullRequest_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.CreateWebSourceAsync(null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateWebSourceAsync_ValidRequest_ReturnsOk()
    {
        // Arrange
        var existingSource = new WebSource { Id = 1, Name = "Old Name" };
        var request = new UpdateWebSourceRequest { Name = "New Name" };

        _mockRepo.Setup(r => r.GetWebSourceByIdAsync(1)).ReturnsAsync(existingSource);
        _mockRepo.Setup(r => r.UpdateWebSourceAsync(It.IsAny<WebSource>())).ReturnsAsync(true);

        // Act
        var result = await _controller.UpdateWebSourceAsync(1, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedSource = okResult.Value.Should().BeAssignableTo<WebSource>().Subject;
        returnedSource.Name.Should().Be("New Name");
    }

    [Fact]
    public async Task DeleteWebSourceAsync_ValidId_ReturnsNoContent()
    {
        // Arrange
        var existingSource = new WebSource { Id = 1, Name = "Source 1" };
        _mockRepo.Setup(r => r.GetWebSourceByIdAsync(1)).ReturnsAsync(existingSource);
        _mockRepo.Setup(r => r.DeleteWebSourceAsync(1)).ReturnsAsync(true);

        // Act
        var result = await _controller.DeleteWebSourceAsync(1);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }
}
