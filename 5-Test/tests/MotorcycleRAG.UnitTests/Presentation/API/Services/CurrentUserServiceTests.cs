using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.API.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Presentation.API.Services;

public class CurrentUserServiceTests
{
    private readonly Mock<IUserProvisioningService> _userProvisioningService = new();
    private readonly Mock<ILogger<CurrentUserService>> _logger = new();

    [Fact]
    public async Task GetManagedUserAsync_WhenEmailClaimMissing_ReturnsNull()
    {
        var service = CreateService(CreateAuthenticatedHttpContext(new Claim(ClaimTypes.NameIdentifier, "subject-1")));

        var result = await service.GetManagedUserAsync();

        result.Should().BeNull();
        _userProvisioningService.Verify(
            provisioning => provisioning.ReconcileApprovedUserAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<IdentityProvider>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task GetManagedUserAsync_WhenGoogleIdentityIsAuthenticated_UsesGoogleProvider()
    {
        var expectedUser = new UserDTO { Id = "managed-user-1", Email = "rider@example.com" };
        _userProvisioningService
            .Setup(provisioning => provisioning.ReconcileApprovedUserAsync(
                "https://issuer.example",
                "subject-1",
                "rider@example.com",
                "Road Runner",
                "Road",
                "Runner",
                IdentityProvider.Google,
                "object-id-1",
                "object-id-1"))
            .ReturnsAsync(expectedUser);

        var service = CreateService(CreateAuthenticatedHttpContext(
            new Claim("iss", "https://issuer.example"),
            new Claim("sub", "subject-1"),
            new Claim(ClaimTypes.Email, "rider@example.com"),
            new Claim(ClaimTypes.Name, "Road Runner"),
            new Claim(ClaimTypes.GivenName, "Road"),
            new Claim(ClaimTypes.Surname, "Runner"),
            new Claim("idp", "google.com"),
            new Claim("oid", "object-id-1")));

        var result = await service.GetManagedUserAsync();

        result.Should().BeSameAs(expectedUser);
    }

    private CurrentUserService CreateService(HttpContext httpContext)
    {
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(accessor => accessor.HttpContext).Returns(httpContext);

        return new CurrentUserService(
            httpContextAccessor.Object,
            _userProvisioningService.Object,
            _logger.Object);
    }

    private static HttpContext CreateAuthenticatedHttpContext(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuthType");
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
    }
}