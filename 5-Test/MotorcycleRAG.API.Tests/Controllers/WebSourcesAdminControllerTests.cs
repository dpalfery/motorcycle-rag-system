using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
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
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        Func<object> nullService = () => new WebSourcesAdminController(null!, _mockControllerLogger.Object);
        Func<object> nullLogger = () => new WebSourcesAdminController(_webSourceRegistryService, null!);
        nullService.Invoking(factory => factory()).Should().Throw<ArgumentNullException>();
        nullLogger.Invoking(factory => factory()).Should().Throw<ArgumentNullException>();
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
    public async Task CreateWebSourceAsync_WhenOptionalValuesAreOmitted_UsesDocumentedDefaults()
    {
        WebSource? submitted = null;
        _mockRepo.Setup(r => r.GetWebSourceByUrlAsync(It.IsAny<Uri>())).ReturnsAsync((WebSource)null!);
        _mockRepo.Setup(r => r.CreateWebSourceAsync(It.IsAny<WebSource>()))
            .Callback<WebSource>(source => submitted = source)
            .ReturnsAsync((WebSource source) => source);

        var result = await _controller.CreateWebSourceAsync(ValidCreateRequest());

        result.Should().BeOfType<CreatedResult>();
        submitted.Should().NotBeNull();
        submitted!.Description.Should().BeEmpty();
        submitted.IsEnabled.Should().BeTrue();
        submitted.TrustTier.Should().Be(3);
        submitted.CrawlFrequencyHours.Should().Be(24);
        submitted.IncludeInSearch.Should().BeTrue();
        submitted.MaxCrawlDepth.Should().Be(2);
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

    // === Validation branch coverage (ValidateCreateRequest / ValidateUpdateRequest) ===

    [Theory]
    [InlineData("urlNull", "URL is required")]
    [InlineData("urlScheme", "URL must use HTTP or HTTPS scheme")]
    [InlineData("nameEmpty", "Name is required")]
    [InlineData("nameLong", "Name cannot exceed 255 characters")]
    [InlineData("descLong", "Description cannot exceed 1000 characters")]
    [InlineData("trustLow", "Trust tier must be between 1 and 5")]
    [InlineData("trustHigh", "Trust tier must be between 1 and 5")]
    [InlineData("crawlZero", "Crawl frequency must be at least 1 hour")]
    [InlineData("depthLow", "Max crawl depth must be between 1 and 10")]
    [InlineData("depthHigh", "Max crawl depth must be between 1 and 10")]
    public async Task CreateWebSourceAsync_WhenValidationFails_ReturnsBadRequestWithErrors(string scenario, string expected)
    {
        var request = ValidCreateRequest();
        switch (scenario)
        {
            case "urlNull": request.Url = null; break;
            case "urlScheme": request.Url = new Uri("ftp://host"); break;
            case "nameEmpty": request.Name = " "; break;
            case "nameLong": request.Name = new string('n', 256); break;
            case "descLong": request.Description = new string('d', 1001); break;
            case "trustLow": request.TrustTier = 0; break;
            case "trustHigh": request.TrustTier = 6; break;
            case "crawlZero": request.CrawlFrequencyHours = 0; break;
            case "depthLow": request.MaxCrawlDepth = 0; break;
            case "depthHigh": request.MaxCrawlDepth = 11; break;
        }

        var result = await _controller.CreateWebSourceAsync(request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { errors = new[] { expected } });
    }

    [Theory]
    [InlineData("urlScheme", "URL must use HTTP or HTTPS scheme")]
    [InlineData("nameLong", "Name cannot exceed 255 characters")]
    [InlineData("descLong", "Description cannot exceed 1000 characters")]
    [InlineData("trustLow", "Trust tier must be between 1 and 5")]
    [InlineData("crawlZero", "Crawl frequency must be at least 1 hour")]
    [InlineData("depthLow", "Max crawl depth must be between 1 and 10")]
    public async Task UpdateWebSourceAsync_WhenValidationFails_ReturnsBadRequestWithErrors(string scenario, string expected)
    {
        var request = new UpdateWebSourceRequest();
        switch (scenario)
        {
            case "urlScheme": request.Url = new Uri("ftp://host"); break;
            case "nameLong": request.Name = new string('n', 256); break;
            case "descLong": request.Description = new string('d', 1001); break;
            case "trustLow": request.TrustTier = 0; break;
            case "crawlZero": request.CrawlFrequencyHours = 0; break;
            case "depthLow": request.MaxCrawlDepth = 0; break;
        }

        var result = await _controller.UpdateWebSourceAsync(1, request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { errors = new[] { expected } });
    }

    [Fact]
    public async Task UpdateWebSourceAsync_InvalidId_ReturnsBadRequest() =>
        (await _controller.UpdateWebSourceAsync(0, new UpdateWebSourceRequest())).Should().BeOfType<BadRequestObjectResult>();

    [Fact]
    public async Task UpdateWebSourceAsync_NullRequest_ReturnsBadRequest() =>
        (await _controller.UpdateWebSourceAsync(1, null!)).Should().BeOfType<BadRequestObjectResult>();

    [Fact]
    public async Task DeleteWebSourceAsync_InvalidId_ReturnsBadRequest() =>
        (await _controller.DeleteWebSourceAsync(0)).Should().BeOfType<BadRequestObjectResult>();

    // === Exception / edge branch coverage ===

    [Fact]
    public async Task GetAllWebSourcesAsync_WhenRepoThrows_ReturnsInternalServerError()
    {
        _mockRepo.Setup(r => r.GetAllWebSourcesAsync()).ThrowsAsync(new InvalidOperationException());
        var result = await _controller.GetAllWebSourcesAsync();
        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task GetWebSourceByIdAsync_WhenRepoThrows_ReturnsInternalServerError()
    {
        _mockRepo.Setup(r => r.GetWebSourceByIdAsync(1)).ThrowsAsync(new InvalidOperationException());
        var result = await _controller.GetWebSourceByIdAsync(1);
        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task CreateWebSourceAsync_WhenUrlAlreadyExists_ReturnsConflict()
    {
        _mockRepo.Setup(r => r.GetWebSourceByUrlAsync(It.IsAny<Uri>())).ReturnsAsync(new WebSource { Id = 9 });
        var result = await _controller.CreateWebSourceAsync(ValidCreateRequest());
        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task CreateWebSourceAsync_WhenRepoThrows_ReturnsInternalServerError()
    {
        // A non-InvalidOperationException surfaces as 500 (InvalidOperationException would map to 409 Conflict).
        _mockRepo.Setup(r => r.GetWebSourceByUrlAsync(It.IsAny<Uri>())).ThrowsAsync(new ApplicationException("boom"));
        var result = await _controller.CreateWebSourceAsync(ValidCreateRequest());
        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task UpdateWebSourceAsync_PatchesAllProvidedFields_ReturnsOk()
    {
        var existing = new WebSource { Id = 1, Name = "Old", Description = "Old", TrustTier = 3 };
        _mockRepo.Setup(r => r.GetWebSourceByIdAsync(1)).ReturnsAsync(existing);
        _mockRepo.Setup(r => r.UpdateWebSourceAsync(It.IsAny<WebSource>())).ReturnsAsync(true);

        var request = new UpdateWebSourceRequest
        {
            Url = new Uri("https://new.example.com"),
            Name = "New",
            Description = "New desc",
            IsEnabled = false,
            TrustTier = 5,
            CrawlFrequencyHours = 12,
            IncludeInSearch = false,
            MaxCrawlDepth = 4
        };

        var result = await _controller.UpdateWebSourceAsync(1, request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var updated = ok.Value.Should().BeOfType<WebSource>().Subject;
        updated.Url.Should().Be("https://new.example.com/");
        updated.Name.Should().Be("New");
        updated.Description.Should().Be("New desc");
        updated.IsEnabled.Should().BeFalse();
        updated.TrustTier.Should().Be(5);
        updated.CrawlFrequencyHours.Should().Be(12);
        updated.IncludeInSearch.Should().BeFalse();
        updated.MaxCrawlDepth.Should().Be(4);
    }

    [Fact]
    public async Task UpdateWebSourceAsync_WhenUpdateFails_ReturnsConflict()
    {
        _mockRepo.Setup(r => r.GetWebSourceByIdAsync(1)).ReturnsAsync(new WebSource { Id = 1 });
        _mockRepo.Setup(r => r.UpdateWebSourceAsync(It.IsAny<WebSource>())).ReturnsAsync(false);

        var result = await _controller.UpdateWebSourceAsync(1, new UpdateWebSourceRequest { Name = "New" });

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task UpdateWebSourceAsync_WhenSourceIsMissing_ReturnsNotFound()
    {
        _mockRepo.Setup(r => r.GetWebSourceByIdAsync(1)).ReturnsAsync((WebSource)null!);

        (await _controller.UpdateWebSourceAsync(1, new UpdateWebSourceRequest { Name = "New" }))
            .Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UpdateWebSourceAsync_WhenServiceRejectsArguments_ReturnsBadRequest()
    {
        _mockRepo.Setup(r => r.GetWebSourceByIdAsync(1)).ReturnsAsync(new WebSource { Id = 1 });
        _mockRepo.Setup(r => r.UpdateWebSourceAsync(It.IsAny<WebSource>())).ThrowsAsync(new ArgumentException("invalid"));

        (await _controller.UpdateWebSourceAsync(1, new UpdateWebSourceRequest { Name = "New" }))
            .Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateWebSourceAsync_WhenRepoThrows_ReturnsInternalServerError()
    {
        _mockRepo.Setup(r => r.GetWebSourceByIdAsync(1)).ReturnsAsync(new WebSource { Id = 1 });
        // A non-InvalidOperationException surfaces as 500 (InvalidOperationException would map to 409 Conflict).
        _mockRepo.Setup(r => r.UpdateWebSourceAsync(It.IsAny<WebSource>())).ThrowsAsync(new ApplicationException("boom"));

        var result = await _controller.UpdateWebSourceAsync(1, new UpdateWebSourceRequest { Name = "New" });

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task DeleteWebSourceAsync_WhenDeleteFails_ReturnsInternalServerErrorUnableToDelete()
    {
        _mockRepo.Setup(r => r.GetWebSourceByIdAsync(1)).ReturnsAsync(new WebSource { Id = 1 });
        _mockRepo.Setup(r => r.DeleteWebSourceAsync(1)).ReturnsAsync(false);

        var result = await _controller.DeleteWebSourceAsync(1);

        var serverError = result.Should().BeOfType<ObjectResult>().Subject;
        serverError.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        serverError.Value.Should().BeEquivalentTo(new { error = "Unable to delete this resource" });
    }

    [Fact]
    public async Task DeleteWebSourceAsync_WhenSourceIsMissing_ReturnsNotFound()
    {
        _mockRepo.Setup(r => r.GetWebSourceByIdAsync(1)).ReturnsAsync((WebSource)null!);

        (await _controller.DeleteWebSourceAsync(1)).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteWebSourceAsync_WhenRepoThrows_ReturnsInternalServerError()
    {
        _mockRepo.Setup(r => r.GetWebSourceByIdAsync(1)).ReturnsAsync(new WebSource { Id = 1 });
        _mockRepo.Setup(r => r.DeleteWebSourceAsync(1)).ThrowsAsync(new InvalidOperationException());

        var result = await _controller.DeleteWebSourceAsync(1);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task DeleteWebSourceAsync_WhenLookupFailsUnexpectedly_ReturnsGenericInternalServerError()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetWebSourceByIdAsync(1)).ThrowsAsync(new ApplicationException("lookup failed"));

        // Act
        var result = await _controller.DeleteWebSourceAsync(1);

        // Assert
        var serverError = result.Should().BeOfType<ObjectResult>().Subject;
        serverError.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        serverError.Value.Should().BeEquivalentTo(new { error = "An error occurred" });
    }

    private static CreateWebSourceRequest ValidCreateRequest() => new()
    {
        Url = new Uri("https://example.com"),
        Name = "Example"
    };
}
