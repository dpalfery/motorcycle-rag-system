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

public class UsersAdminControllerTests
{
    private readonly Mock<IUserAdminService> _mockUserAdminService;
    private readonly Mock<ICurrentUserService> _mockUserService;
    private readonly Mock<UserAccessLifecycleService> _mockLifecycleService;
    private readonly Mock<ILogger<UsersAdminController>> _mockLogger;
    private readonly UsersAdminController _controller;

    public UsersAdminControllerTests()
    {
        _mockUserAdminService = new Mock<IUserAdminService>();
        _mockUserService = new Mock<ICurrentUserService>();
        
        // Mock dependencies for UserAccessLifecycleService
        var mockUserRepo = new Mock<IUserRepository>();
        var mockUserIdentityRepo = new Mock<IUserIdentityRepository>();
        var mockPlanRepo = new Mock<IPlanRepository>();
        var mockQueryRepo = new Mock<IUserManagementQueryRepository>();
        var mockIdentityService = new Mock<IExternalIdentityProvisioningService>();
        var mockTelemetryService = new Mock<ITelemetryService>();
        var mockLifecycleLogger = new Mock<ILogger<UserAccessLifecycleService>>();
        
        _mockLifecycleService = new Mock<UserAccessLifecycleService>(
            mockUserRepo.Object,
            mockUserIdentityRepo.Object,
            mockPlanRepo.Object,
            mockQueryRepo.Object,
            mockIdentityService.Object,
            new TierEntitlementMappingService(new Mock<ILogger<TierEntitlementMappingService>>().Object), // concrete class
            mockTelemetryService.Object,
            mockLifecycleLogger.Object
        );

        _mockLogger = new Mock<ILogger<UsersAdminController>>();

        _controller = new UsersAdminController(
            _mockUserAdminService.Object,
            _mockUserService.Object,
            _mockLifecycleService.Object,
            _mockLogger.Object);
            
        // Setup admin user
        _mockUserService.Setup(u => u.IsAuthenticated).Returns(true);
        _mockUserService.Setup(u => u.IsInRole("mcr-api-admin")).Returns(true);
        _mockUserService.Setup(u => u.UserId).Returns("admin-id");
        _mockUserService.Setup(u => u.GetManagedUserIdAsync()).ReturnsAsync("managed-admin-id");
    }

    [Fact]
    public async Task SetUserEnabledStatusAsync_ValidRequest_ReturnsOk()
    {
        // Arrange
        var userId = "test-user-id";
        var request = new SetUserEnabledRequest { IsEnabled = true };
        var updatedUser = new UserDTO { Id = userId, IsEnabled = true };
        
        _mockUserAdminService.Setup(s => s.SetUserEnabledStatusAsync(userId, true)).ReturnsAsync(updatedUser);

        // Act
        var result = await _controller.SetUserEnabledStatusAsync(userId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedUser = okResult.Value.Should().BeAssignableTo<UserDTO>().Subject;
        returnedUser.Id.Should().Be(userId);
        returnedUser.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task SetUserEnabledStatusAsync_NullRequest_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.SetUserEnabledStatusAsync("test-user", null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }
    
    [Fact]
    public async Task SetUserEnabledStatusAsync_EmptyUserId_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.SetUserEnabledStatusAsync("", new SetUserEnabledRequest());

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task AssignPlanToUserAsync_ValidRequest_ReturnsOk()
    {
        // Arrange
        var userId = "test-user-id";
        var planId = "plan-id";
        var request = new AssignPlanRequest { PlanId = planId };
        var updatedUser = new UserDTO { Id = userId, PlanId = planId };
        
        _mockUserAdminService.Setup(s => s.AssignPlanToUserAsync(userId, planId)).ReturnsAsync(updatedUser);

        // Act
        var result = await _controller.AssignPlanToUserAsync(userId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedUser = okResult.Value.Should().BeAssignableTo<UserDTO>().Subject;
        returnedUser.PlanId.Should().Be(planId);
    }
    
    [Fact]
    public async Task AssignPlanToUserAsync_NullRequest_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.AssignPlanToUserAsync("test-user", null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ChangeManagedUserTierAsync_ValidRequest_ReturnsOk()
    {
        // Arrange
        var userId = "test-user-id";
        var request = new ChangeManagedUserTierRequest { Tier = TierLabel.RoadRunner, ExpectedRowVersion = "v1" };
        var adminResponse = new AdminActionResponse { Row = new UserManagementRow() };
        
        _mockLifecycleService.Setup(s => s.ChangeManagedUserTierAsync(userId, request)).ReturnsAsync(adminResponse);

        // Act
        var result = await _controller.ChangeManagedUserTierAsync(userId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedResponse = okResult.Value.Should().BeAssignableTo<AdminActionResponse>().Subject;
        returnedResponse.Should().Be(adminResponse);
    }
    
    [Fact]
    public async Task ChangeManagedUserTierAsync_NullRequest_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.ChangeManagedUserTierAsync("test-user", null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CancelManagedUserAsync_ValidRequest_ReturnsOk()
    {
        // Arrange
        var userId = "test-user-id";
        var request = new CancelManagedUserRequest { Reason = "Requested", ExpectedRowVersion = "v1" };
        var adminResponse = new AdminActionResponse { Row = new UserManagementRow() };
        
        _mockLifecycleService.Setup(s => s.CancelManagedUserAsync(userId, request, "managed-admin-id")).ReturnsAsync(adminResponse);

        // Act
        var result = await _controller.CancelManagedUserAsync(userId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedResponse = okResult.Value.Should().BeAssignableTo<AdminActionResponse>().Subject;
        returnedResponse.Should().Be(adminResponse);
    }

    [Fact]
    public async Task GetAllUsersAsync_ValidRequest_ReturnsOk()
    {
        // Arrange
        var users = new[] { new UserDTO { Id = "test-user" } };
        _mockUserAdminService.Setup(s => s.GetAllUsersAsync(1, 10)).ReturnsAsync(users);

        // Act
        var result = await _controller.GetAllUsersAsync(1, 10);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<UserListResponse>().Subject;
        response.TotalCount.Should().Be(1);
        response.Page.Should().Be(1);
        response.PageSize.Should().Be(10);
    }
    
    [Fact]
    public async Task GetAllUsersAsync_InvalidPage_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetAllUsersAsync(0, 10);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }
    
    [Fact]
    public async Task GetAllUsersAsync_InvalidPageSize_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetAllUsersAsync(1, 200);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
