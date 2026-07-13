using System.Security.Claims;
using FluentAssertions;
using MotorcycleRAG.API.Configuration.Services;
using Xunit;

namespace MotorcycleRAG.UnitTests.Presentation.API.Configuration.Services;

public class RateLimitingServiceConfigurationTests
{
    [Fact]
    public void GetRateLimitForUser_Unauthenticated_Returns50()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var result = RateLimitingServiceConfiguration.GetRateLimitForUser(user);

        // Assert
        result.Should().Be(50);
    }

    [Fact]
    public void GetRateLimitForUser_AdminRole_Returns100000()
    {
        // Arrange
        var claims = new[] { new Claim(ClaimTypes.Role, "mcr-api-admin") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act
        var result = RateLimitingServiceConfiguration.GetRateLimitForUser(user);

        // Assert
        result.Should().Be(100000);
    }

    [Fact]
    public void GetRateLimitForUser_RoadrunnerRole_Returns100000()
    {
        // Arrange
        var claims = new[] { new Claim("roles", "Roadrunner") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act
        var result = RateLimitingServiceConfiguration.GetRateLimitForUser(user);

        // Assert
        result.Should().Be(100000);
    }

    [Fact]
    public void GetRateLimitForUser_ProUserRole_Returns500()
    {
        // Arrange
        var claims = new[] { new Claim(ClaimTypes.Role, "ProUser") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act
        var result = RateLimitingServiceConfiguration.GetRateLimitForUser(user);

        // Assert
        result.Should().Be(500);
    }

    [Fact]
    public void GetRateLimitForUser_RegularUser_Returns50()
    {
        // Arrange
        var claims = new[] { new Claim(ClaimTypes.Role, "User") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act
        var result = RateLimitingServiceConfiguration.GetRateLimitForUser(user);

        // Assert
        result.Should().Be(50);
    }
}
