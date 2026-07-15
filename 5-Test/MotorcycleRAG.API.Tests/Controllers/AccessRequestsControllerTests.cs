using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.API.Tests.Controllers;

public class AccessRequestsControllerTests
{
    private readonly Mock<AccessRequestService> _mockService;
    private readonly Mock<ILogger<AccessRequestsController>> _mockLogger;
    private readonly AccessRequestsController _controller;

    public AccessRequestsControllerTests()
    {
        var mockRepo = new Mock<IAccessRequestRepository>();
        var mockNotifier = new Mock<IApproverNotificationService>();
        var mockCorrelation = new Mock<ICorrelationService>();
        var mockTelemetry = new Mock<ITelemetryService>();
        var mockOptions = new Mock<IOptions<OnboardingOptions>>();
        mockOptions.Setup(o => o.Value).Returns(new OnboardingOptions());
        var mockServiceLogger = new Mock<ILogger<AccessRequestService>>();

        _mockService = new Mock<AccessRequestService>(
            mockRepo.Object,
            mockNotifier.Object,
            mockCorrelation.Object,
            mockTelemetry.Object,
            mockOptions.Object,
            mockServiceLogger.Object
        );

        _mockLogger = new Mock<ILogger<AccessRequestsController>>();

        _controller = new AccessRequestsController(
            _mockService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        Func<object> nullService = () => new AccessRequestsController(null!, _mockLogger.Object);
        Func<object> nullLogger = () => new AccessRequestsController(_mockService.Object, null!);
        nullService.Invoking(factory => factory()).Should().Throw<ArgumentNullException>();
        nullLogger.Invoking(factory => factory()).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CreateAsync_NewRequest_ReturnsAccepted()
    {
        // Arrange
        var request = new CreateAccessRequestRequest { Email = "test@example.com", Provider = IdentityProvider.Google };
        var response = new PublicAccessRequestResponse { RequestId = "req-1" };
        
        _mockService.Setup(s => s.CreateOrGetExistingAsync(request)).ReturnsAsync((response, true));

        // Act
        var result = await _controller.CreateAsync(request);

        // Assert
        var acceptedResult = result.Should().BeOfType<AcceptedResult>().Subject;
        var returnedResponse = acceptedResult.Value.Should().BeAssignableTo<PublicAccessRequestResponse>().Subject;
        returnedResponse.Should().Be(response);
    }
    
    [Fact]
    public async Task CreateAsync_ExistingRequest_ReturnsConflict()
    {
        // Arrange
        var request = new CreateAccessRequestRequest { Email = "test@example.com", Provider = IdentityProvider.Google };
        var response = new PublicAccessRequestResponse { RequestId = "req-1" };
        
        _mockService.Setup(s => s.CreateOrGetExistingAsync(request)).ReturnsAsync((response, false));

        // Act
        var result = await _controller.CreateAsync(request);

        // Assert
        var conflictResult = result.Should().BeOfType<ConflictObjectResult>().Subject;
        var returnedResponse = conflictResult.Value.Should().BeAssignableTo<PublicAccessRequestResponse>().Subject;
        returnedResponse.Should().Be(response);
    }

    [Fact]
    public async Task CreateAsync_NullRequest_ReturnsBadRequest()
    {
        // Act
        // Bypassing ASP.NET Core model binding checks by passing null directly
        Func<Task> act = async () => await _controller.CreateAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
    
    [Fact]
    public async Task CreateAsync_InvalidModelState_ReturnsBadRequest()
    {
        // Arrange
        _controller.ModelState.AddModelError("Email", "Required");
        
        var request = new CreateAccessRequestRequest();

        // Act
        var result = await _controller.CreateAsync(request);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        var problemDetails = objectResult.Value.Should().BeOfType<ValidationProblemDetails>().Subject;
        problemDetails.Errors.Should().NotBeEmpty();
    }
    
    [Fact]
    public async Task CreateAsync_ArgumentException_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateAccessRequestRequest { Email = "test@example.com", Provider = IdentityProvider.Google };
        _mockService.Setup(s => s.CreateOrGetExistingAsync(request)).ThrowsAsync(new ArgumentException("Invalid email"));

        // Act
        var result = await _controller.CreateAsync(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
