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
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.API.Tests.Controllers;

public class AccessRequestsAdminControllerTests
{
    private readonly Mock<AccessRequestAdminService> _mockAdminService;
    private readonly Mock<ICurrentUserService> _mockUserService;
    private readonly Mock<ILogger<AccessRequestsAdminController>> _mockLogger;
    private readonly AccessRequestsAdminController _controller;

    public AccessRequestsAdminControllerTests()
    {
        // Mock dependencies for ApprovalOnboardingService
        var mockAccessRepo = new Mock<IAccessRequestRepository>();
        var mockUserRepo = new Mock<IUserRepository>();
        var mockUserIdentityRepo = new Mock<IUserIdentityRepository>();
        var mockPlanRepo = new Mock<IPlanRepository>();
        var mockUsageTracking = new Mock<IUsageTrackingService>();
        var mockIdentityService = new Mock<IExternalIdentityProvisioningService>();
        var mockTelemetry = new Mock<ITelemetryService>();
        var tierMapping = new TierEntitlementMappingService(new Mock<ILogger<TierEntitlementMappingService>>().Object);
        
        var mockApprovalService = new Mock<ApprovalOnboardingService>(
            mockAccessRepo.Object,
            mockUserRepo.Object,
            mockUserIdentityRepo.Object,
            mockPlanRepo.Object,
            mockUsageTracking.Object,
            mockIdentityService.Object,
            tierMapping,
            mockTelemetry.Object,
            new Mock<ILogger<ApprovalOnboardingService>>().Object
        );

        // Mock dependencies for UserAccessLifecycleService
        var mockQueryRepo = new Mock<IUserManagementQueryRepository>();
        var mockLifecycleService = new Mock<UserAccessLifecycleService>(
            mockUserRepo.Object,
            mockUserIdentityRepo.Object,
            mockPlanRepo.Object,
            mockQueryRepo.Object,
            mockIdentityService.Object,
            tierMapping,
            mockTelemetry.Object,
            new Mock<ILogger<UserAccessLifecycleService>>().Object
        );

        _mockAdminService = new Mock<AccessRequestAdminService>(
            mockAccessRepo.Object,
            mockQueryRepo.Object,
            mockApprovalService.Object,
            mockLifecycleService.Object,
            mockTelemetry.Object,
            new Mock<ILogger<AccessRequestAdminService>>().Object
        );

        _mockUserService = new Mock<ICurrentUserService>();
        _mockLogger = new Mock<ILogger<AccessRequestsAdminController>>();

        _controller = new AccessRequestsAdminController(
            _mockAdminService.Object,
            _mockUserService.Object,
            _mockLogger.Object);

        // Setup admin user
        _mockUserService.Setup(u => u.IsAuthenticated).Returns(true);
        _mockUserService.Setup(u => u.IsInRole("mcr-api-admin")).Returns(true);
        _mockUserService.Setup(u => u.UserId).Returns("admin-id");
        _mockUserService.Setup(u => u.GetManagedUserIdAsync()).ReturnsAsync("managed-admin-id");
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        Func<object> nullService = () => new AccessRequestsAdminController(null!, _mockUserService.Object, _mockLogger.Object);
        Func<object> nullUser = () => new AccessRequestsAdminController(_mockAdminService.Object, null!, _mockLogger.Object);
        Func<object> nullLogger = () => new AccessRequestsAdminController(_mockAdminService.Object, _mockUserService.Object, null!);
        nullService.Invoking(factory => factory()).Should().Throw<ArgumentNullException>();
        nullUser.Invoking(factory => factory()).Should().Throw<ArgumentNullException>();
        nullLogger.Invoking(factory => factory()).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task GetUserManagementAsync_ValidRequest_ReturnsOk()
    {
        // Arrange
        var response = new UserManagementListResponse { Rows = new[] { new UserManagementRow() }, TotalCount = 1 };
        _mockAdminService.Setup(s => s.GetUserManagementRowsAsync(null, null, 1, 50)).ReturnsAsync(response);

        // Act
        var result = await _controller.GetUserManagementAsync(null, null, 1, 50);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedResponse = okResult.Value.Should().BeAssignableTo<UserManagementListResponse>().Subject;
        returnedResponse.Should().Be(response);
    }
    
    [Fact]
    public async Task GetUserManagementAsync_InvalidArgs_ReturnsBadRequest()
    {
        // Arrange
        _mockAdminService.Setup(s => s.GetUserManagementRowsAsync(null, null, 0, 50))
            .ThrowsAsync(new ArgumentException("Invalid"));

        // Act
        var result = await _controller.GetUserManagementAsync(null, null, 0, 50);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ApproveAccessRequestAsync_ValidRequest_ReturnsOk()
    {
        // Arrange
        var requestId = "req-1";
        var request = new ApproveAccessRequestRequest { Tier = TierLabel.RoadRunner, ExpectedRowVersion = "v1" };
        var adminResponse = new AdminActionResponse { Row = new UserManagementRow() };
        
        _mockAdminService.Setup(s => s.ApproveAccessRequestAsync(requestId, request, "managed-admin-id"))
            .ReturnsAsync(adminResponse);

        // Act
        var result = await _controller.ApproveAccessRequestAsync(requestId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedResponse = okResult.Value.Should().BeAssignableTo<AdminActionResponse>().Subject;
        returnedResponse.Should().Be(adminResponse);
    }

    [Fact]
    public async Task ApproveAccessRequestAsync_NullRequest_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.ApproveAccessRequestAsync("req-1", null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }
    
    [Fact]
    public async Task ApproveAccessRequestAsync_EmptyId_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.ApproveAccessRequestAsync("", new ApproveAccessRequestRequest());

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RetryOnboardingAsync_ValidRequest_ReturnsOk()
    {
        // Arrange
        var requestId = "req-1";
        var request = new RetryAccessRequestOnboardingRequest { ExpectedRowVersion = "v1" };
        var adminResponse = new AdminActionResponse { Row = new UserManagementRow() };
        
        _mockAdminService.Setup(s => s.RetryOnboardingAsync(requestId, request))
            .ReturnsAsync(adminResponse);

        // Act
        var result = await _controller.RetryOnboardingAsync(requestId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedResponse = okResult.Value.Should().BeAssignableTo<AdminActionResponse>().Subject;
        returnedResponse.Should().Be(adminResponse);
    }
    
    [Fact]
    public async Task RetryOnboardingAsync_NullRequest_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.RetryOnboardingAsync("req-1", null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CancelAccessRequestAsync_ValidRequest_ReturnsOk()
    {
        // Arrange
        var requestId = "req-1";
        var request = new CancelAccessRequestRequest { Reason = "Spam", ExpectedRowVersion = "v1" };
        var adminResponse = new AdminActionResponse { Row = new UserManagementRow() };
        
        _mockAdminService.Setup(s => s.CancelAccessRequestAsync(requestId, request, "managed-admin-id"))
            .ReturnsAsync(adminResponse);

        // Act
        var result = await _controller.CancelAccessRequestAsync(requestId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedResponse = okResult.Value.Should().BeAssignableTo<AdminActionResponse>().Subject;
        returnedResponse.Should().Be(adminResponse);
    }
    
    [Fact]
    public async Task CancelAccessRequestAsync_NullRequest_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.CancelAccessRequestAsync("req-1", null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ApproveAccessRequestAsync_ServiceRejectsRequest_MapsBadRequestAndConflict()
    {
        var request = new ApproveAccessRequestRequest { Tier = TierLabel.RoadRunner, ExpectedRowVersion = "v1" };
        _mockAdminService.Setup(s => s.ApproveAccessRequestAsync("req-1", request, "managed-admin-id"))
            .ThrowsAsync(new ArgumentException("invalid"));
        (await _controller.ApproveAccessRequestAsync("req-1", request)).Should().BeOfType<BadRequestObjectResult>();

        _mockAdminService.Setup(s => s.ApproveAccessRequestAsync("req-1", request, "managed-admin-id"))
            .ThrowsAsync(new InvalidOperationException("conflict"));
        (await _controller.ApproveAccessRequestAsync("req-1", request)).Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task RetryOnboardingAsync_GuardsAndServiceFailures_MapExpectedResults()
    {
        var request = new RetryAccessRequestOnboardingRequest { ExpectedRowVersion = "v1" };
        (await _controller.RetryOnboardingAsync(" ", request)).Should().BeOfType<BadRequestObjectResult>();

        _mockAdminService.Setup(s => s.RetryOnboardingAsync("req-1", request)).ThrowsAsync(new ArgumentException("invalid"));
        (await _controller.RetryOnboardingAsync("req-1", request)).Should().BeOfType<BadRequestObjectResult>();

        _mockAdminService.Setup(s => s.RetryOnboardingAsync("req-1", request)).ThrowsAsync(new InvalidOperationException("conflict"));
        (await _controller.RetryOnboardingAsync("req-1", request)).Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task CancelAccessRequestAsync_GuardsAndServiceFailures_MapExpectedResults()
    {
        var request = new CancelAccessRequestRequest { Reason = "reason", ExpectedRowVersion = "v1" };
        (await _controller.CancelAccessRequestAsync(" ", request)).Should().BeOfType<BadRequestObjectResult>();
        (await _controller.CancelAccessRequestAsync("req-1", new CancelAccessRequestRequest { Reason = " " })).Should().BeOfType<BadRequestObjectResult>();

        _mockAdminService.Setup(s => s.CancelAccessRequestAsync("req-1", request, "managed-admin-id"))
            .ThrowsAsync(new ArgumentException("invalid"));
        (await _controller.CancelAccessRequestAsync("req-1", request)).Should().BeOfType<BadRequestObjectResult>();

        _mockAdminService.Setup(s => s.CancelAccessRequestAsync("req-1", request, "managed-admin-id"))
            .ThrowsAsync(new InvalidOperationException("conflict"));
        (await _controller.CancelAccessRequestAsync("req-1", request)).Should().BeOfType<ConflictObjectResult>();
    }
}
