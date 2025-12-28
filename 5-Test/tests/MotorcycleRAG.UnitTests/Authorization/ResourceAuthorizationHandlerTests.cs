using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.API.Authorization;
using System.Security.Claims;
using Xunit;

namespace MotorcycleRAG.UnitTests.Authorization;

public class ResourceAuthorizationHandlerTests
{
    private readonly Mock<ILogger<ResourceAuthorizationHandler>> _loggerMock;
    private readonly ResourceAuthorizationHandler _handler;

    public ResourceAuthorizationHandlerTests()
    {
        _loggerMock = new Mock<ILogger<ResourceAuthorizationHandler>>();
        _handler = new ResourceAuthorizationHandler(_loggerMock.Object);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_WhenUserHasOneOfRequiredRoles()
    {
        // Arrange
        var requirement = new ResourceAuthorizationRequirement("DataPipeline", "Admin", "DataAdmin");
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Role, "DataAdmin")
        }, "TestAuth"));

        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("User authorized for resource: DataPipeline")),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Fails_WhenUserLacksAllRequiredRoles()
    {
        // Arrange
        var requirement = new ResourceAuthorizationRequirement("DataPipeline", "Admin", "DataAdmin");
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Role, "User")
        }, "TestAuth"));

        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("User lacks required roles for resource: DataPipeline")),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Fails_WhenUserUnauthenticated()
    {
        // Arrange
        var requirement = new ResourceAuthorizationRequirement("DataPipeline", "Admin");
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // Unauthenticated user

        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Unauthenticated user attempted to access resource")),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public void ResourceAuthorizationRequirement_Constructor_Throws_WhenResourceTypeIsNull()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ResourceAuthorizationRequirement(null!, "Admin"));
    }

    [Fact]
    public void ResourceAuthorizationRequirement_Constructor_Throws_WhenRequiredRolesIsEmpty()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new ResourceAuthorizationRequirement("DataPipeline", Array.Empty<string>()));
    }

    [Fact]
    public void ResourceAuthorizationRequirement_Constructor_Throws_WhenRequiredRolesIsNull()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ResourceAuthorizationRequirement("DataPipeline", null!));
    }
}
